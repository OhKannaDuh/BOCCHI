import { isUnloadAltitude } from "./worldBounds";

const MAX_OBSERVATIONS_PER_RUN = 500;
const CLUSTER_RADIUS = 1.5;
const REVIEW_REPORTER_THRESHOLD = 3;
const AUTO_ACCEPT_NOTE = "Automatically accepted after three distinct installation reports.";

interface DatabaseEnv {
  DB: D1Database;
}

interface PendingObservation {
  id: number;
  territory_id: number;
  data_id: number;
  world_x: number;
  world_y: number;
  world_z: number;
  installation_hash: string;
  observed_at_utc: string;
}

interface Candidate {
  id: number;
  centroid_x: number;
  centroid_y: number;
  centroid_z: number;
}

export interface ProcessorResult {
  scanned: number;
  assigned: number;
  failed: number;
  newlyAccepted: number;
}

export async function processObservationById(
  env: DatabaseEnv,
  observationId: number,
): Promise<ProcessorResult> {
  const observation = await env.DB.prepare(`
    SELECT
      o.id,
      o.territory_id,
      o.data_id,
      o.world_x,
      o.world_y,
      o.world_z,
      o.installation_hash,
      o.observed_at_utc
    FROM observations o
    WHERE o.id = ?
      AND o.processed = 0
      AND o.data_id IS NOT NULL
      AND NOT EXISTS (
        SELECT 1
        FROM observation_candidate_members m
        WHERE m.observation_id = o.id
      )
  `).bind(observationId).first<PendingObservation>();

  if (observation === null) {
    return { scanned: 0, assigned: 0, failed: 0, newlyAccepted: 0 };
  }

  return processOne(env.DB, observation);
}

export async function processPendingObservations(env: DatabaseEnv): Promise<ProcessorResult> {
  const pending = await env.DB.prepare(`
    SELECT
      o.id,
      o.territory_id,
      o.data_id,
      o.world_x,
      o.world_y,
      o.world_z,
      o.installation_hash,
      o.observed_at_utc
    FROM observations o
    WHERE o.processed = 0
      AND o.data_id IS NOT NULL
      AND NOT EXISTS (
        SELECT 1
        FROM observation_candidate_members m
        WHERE m.observation_id = o.id
      )
    ORDER BY o.id
    LIMIT ?
  `).bind(MAX_OBSERVATIONS_PER_RUN).all<PendingObservation>();

  let assigned = 0;
  let failed = 0;
  let newlyAccepted = 0;
  for (const observation of pending.results) {
    const result = await processOne(env.DB, observation);
    assigned += result.assigned;
    failed += result.failed;
    newlyAccepted += result.newlyAccepted;
  }

  return {
    scanned: pending.results.length,
    assigned,
    failed,
    newlyAccepted,
  };
}

async function processOne(db: D1Database, observation: PendingObservation): Promise<ProcessorResult> {
  try {
    if (isUnloadAltitude(observation.world_y)) {
      await db.prepare(`
        UPDATE observations
        SET processed = 1
        WHERE id = ?
      `).bind(observation.id).run();
      return { scanned: 1, assigned: 0, failed: 0, newlyAccepted: 0 };
    }

    let candidate = await findCandidate(db, observation);
    if (candidate === null) {
      candidate = await createCandidate(db, observation);
    }

    const newlyAccepted = await assignObservation(db, candidate.id, observation);
    return {
      scanned: 1,
      assigned: 1,
      failed: 0,
      newlyAccepted: newlyAccepted ? 1 : 0,
    };
  } catch (error) {
    console.error(`Failed to process observation ${observation.id}`, error);
    return { scanned: 1, assigned: 0, failed: 1, newlyAccepted: 0 };
  }
}

async function findCandidate(
  db: D1Database,
  observation: PendingObservation,
): Promise<Candidate | null> {
  const candidates = await db.prepare(`
    SELECT id, centroid_x, centroid_y, centroid_z
    FROM observation_candidates
    WHERE territory_id = ?
      AND data_id = ?
      AND status != 'rejected'
      AND ABS(centroid_x - ?) <= ?
      AND ABS(centroid_y - ?) <= ?
      AND ABS(centroid_z - ?) <= ?
  `).bind(
    observation.territory_id,
    observation.data_id,
    observation.world_x,
    CLUSTER_RADIUS,
    observation.world_y,
    CLUSTER_RADIUS,
    observation.world_z,
    CLUSTER_RADIUS,
  ).all<Candidate>();

  const radiusSquared = CLUSTER_RADIUS * CLUSTER_RADIUS;
  return candidates.results
    .map(candidate => ({
      candidate,
      distanceSquared: distanceSquared(candidate, observation),
    }))
    .filter(entry => entry.distanceSquared <= radiusSquared)
    .sort((left, right) => left.distanceSquared - right.distanceSquared)[0]?.candidate ?? null;
}

async function createCandidate(
  db: D1Database,
  observation: PendingObservation,
): Promise<Candidate> {
  const result = await db.prepare(`
    INSERT INTO observation_candidates (
      territory_id,
      data_id,
      centroid_x,
      centroid_y,
      centroid_z,
      first_observed_at_utc,
      last_observed_at_utc
    ) VALUES (?, ?, ?, ?, ?, ?, ?)
  `).bind(
    observation.territory_id,
    observation.data_id,
    observation.world_x,
    observation.world_y,
    observation.world_z,
    observation.observed_at_utc,
    observation.observed_at_utc,
  ).run();

  if (!result.success || result.meta.last_row_id === undefined) {
    throw new Error("Candidate insert failed.");
  }

  return {
    id: result.meta.last_row_id,
    centroid_x: observation.world_x,
    centroid_y: observation.world_y,
    centroid_z: observation.world_z,
  };
}

async function assignObservation(
  db: D1Database,
  candidateId: number,
  observation: PendingObservation,
): Promise<boolean> {
  const inserted = await db.prepare(`
    INSERT OR IGNORE INTO observation_candidate_members (
      candidate_id,
      observation_id,
      installation_hash
    ) VALUES (?, ?, ?)
  `).bind(candidateId, observation.id, observation.installation_hash).run();

  if (!inserted.success) {
    throw new Error("Member insert failed.");
  }

  if ((inserted.meta.changes ?? 0) === 0) {
    await db.prepare(`
      UPDATE observations
      SET processed = 1
      WHERE id = ?
    `).bind(observation.id).run();
    return false;
  }

  const results = await db.batch([
    db.prepare(`
      SELECT status
      FROM observation_candidates
      WHERE id = ?
    `).bind(candidateId),
    db.prepare(`
      WITH delta AS (
        SELECT IIF(
          EXISTS (
            SELECT 1
            FROM observation_candidate_members
            WHERE candidate_id = ?
              AND installation_hash = ?
              AND observation_id != ?
          ),
          0,
          1
        ) AS distinct_delta
      )
      UPDATE observation_candidates
      SET observation_count = observation_count + 1,
        distinct_installation_count = distinct_installation_count
          + (SELECT distinct_delta FROM delta),
        centroid_x = (centroid_x * observation_count + ?) / (observation_count + 1),
        centroid_y = (centroid_y * observation_count + ?) / (observation_count + 1),
        centroid_z = (centroid_z * observation_count + ?) / (observation_count + 1),
        first_observed_at_utc = MIN(first_observed_at_utc, ?),
        last_observed_at_utc = MAX(last_observed_at_utc, ?),
        status = CASE
          WHEN status = 'pending'
            AND distinct_installation_count + (SELECT distinct_delta FROM delta) >= ?
            THEN 'accepted'
          ELSE status
        END,
        acceptance_method = CASE
          WHEN status = 'pending'
            AND distinct_installation_count + (SELECT distinct_delta FROM delta) >= ?
            THEN 'automatic'
          ELSE acceptance_method
        END,
        reviewed_at_utc = CASE
          WHEN status = 'pending'
            AND distinct_installation_count + (SELECT distinct_delta FROM delta) >= ?
            THEN COALESCE(reviewed_at_utc, CURRENT_TIMESTAMP)
          ELSE reviewed_at_utc
        END,
        review_note = CASE
          WHEN status = 'pending'
            AND distinct_installation_count + (SELECT distinct_delta FROM delta) >= ?
            THEN ?
          ELSE review_note
        END,
        updated_at_utc = CURRENT_TIMESTAMP
      WHERE id = ?
      RETURNING status
    `).bind(
      candidateId,
      observation.installation_hash,
      observation.id,
      observation.world_x,
      observation.world_y,
      observation.world_z,
      observation.observed_at_utc,
      observation.observed_at_utc,
      REVIEW_REPORTER_THRESHOLD,
      REVIEW_REPORTER_THRESHOLD,
      REVIEW_REPORTER_THRESHOLD,
      REVIEW_REPORTER_THRESHOLD,
      AUTO_ACCEPT_NOTE,
      candidateId,
    ),
    db.prepare(`
      UPDATE observations
      SET processed = 1
      WHERE id = ?
    `).bind(observation.id),
  ]);

  const previous = results[0].results[0] as { status: string } | undefined;
  if (previous === undefined) {
    throw new Error("Candidate missing during assign.");
  }

  const next = results[1].results[0] as { status: string } | undefined;
  return next !== undefined && previous.status === "pending" && next.status === "accepted";
}

function distanceSquared(candidate: Candidate, observation: PendingObservation): number {
  const deltaX = candidate.centroid_x - observation.world_x;
  const deltaY = candidate.centroid_y - observation.world_y;
  const deltaZ = candidate.centroid_z - observation.world_z;
  return deltaX * deltaX + deltaY * deltaY + deltaZ * deltaZ;
}
