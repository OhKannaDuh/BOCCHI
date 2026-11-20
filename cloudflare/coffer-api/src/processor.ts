import { isUnloadAltitude } from "./worldBounds.ts";

const MAX_OBSERVATIONS_PER_RUN = 500;
const CLUSTER_RADIUS = 1.5;
const CLUSTER_RADIUS_SQUARED = CLUSTER_RADIUS * CLUSTER_RADIUS;
const REVIEW_REPORTER_THRESHOLD = 3;
const AUTO_ACCEPT_NOTE = "Automatically accepted after three different reporters.";

export type ClusterKind = "coffers" | "carrots" | "pot-chests";

interface ClusterTables {
  observations: string;
  candidates: string;
  members: string;
  keys: readonly string[];
  label: string;
}

export const CLUSTER_KINDS: Record<ClusterKind, ClusterTables> = {
  coffers: {
    observations: "observations",
    candidates: "observation_candidates",
    members: "observation_candidate_members",
    keys: ["data_id"],
    label: "observation",
  },
  carrots: {
    observations: "carrot_observations",
    candidates: "carrot_candidates",
    members: "carrot_candidate_members",
    keys: [],
    label: "carrot observation",
  },
  "pot-chests": {
    observations: "pot_chest_observations",
    candidates: "pot_chest_candidates",
    members: "pot_chest_candidate_members",
    keys: ["pot_fate_id", "is_reroll"],
    label: "pot-chest observation",
  },
};

interface DatabaseEnv {
  DB: D1Database;
}

interface PendingObservation {
  id: number;
  territory_id: number;
  world_x: number;
  world_y: number;
  world_z: number;
  installation_hash: string;
  reporter: string;
  observed_at_utc: string;
  linked_candidate: number | null;
  [key: string]: unknown;
}

export interface ProcessorResult {
  scanned: number;
  assigned: number;
  failed: number;
  newlyAccepted: number;
}

function pendingQuery(tables: ClusterTables, byId: boolean): string {
  const keyColumns = tables.keys.map(key => `o.${key},`).join(" ");
  const keyFilters = tables.keys.map(key => `AND o.${key} IS NOT NULL`).join(" ");
  return `
    SELECT o.id, o.territory_id, ${keyColumns}
      o.world_x, o.world_y, o.world_z,
      o.installation_hash,
      COALESCE(o.reporter_hash, o.installation_hash) AS reporter,
      o.observed_at_utc,
      (SELECT m.candidate_id FROM ${tables.members} m WHERE m.observation_id = o.id) AS linked_candidate
    FROM ${tables.observations} o
    WHERE o.processed = 0
      ${keyFilters}
      ${byId ? "AND o.id = ?" : ""}
    ORDER BY o.id
    LIMIT ?
  `;
}

export async function processObservationById(
  env: DatabaseEnv,
  kind: ClusterKind,
  observationId: number,
): Promise<ProcessorResult> {
  const tables = CLUSTER_KINDS[kind];
  const observation = await env.DB.prepare(pendingQuery(tables, true))
    .bind(observationId, 1)
    .first<PendingObservation>();

  if (observation === null) {
    return { scanned: 0, assigned: 0, failed: 0, newlyAccepted: 0 };
  }

  return processOne(env.DB, kind, observation);
}

export async function processPendingObservations(env: DatabaseEnv, kind: ClusterKind): Promise<ProcessorResult> {
  const pending = await env.DB.prepare(pendingQuery(CLUSTER_KINDS[kind], false))
    .bind(MAX_OBSERVATIONS_PER_RUN)
    .all<PendingObservation>();

  const total: ProcessorResult = { scanned: pending.results.length, assigned: 0, failed: 0, newlyAccepted: 0 };
  for (const observation of pending.results) {
    const result = await processOne(env.DB, kind, observation);
    total.assigned += result.assigned;
    total.failed += result.failed;
    total.newlyAccepted += result.newlyAccepted;
  }

  return total;
}

async function processOne(
  db: D1Database,
  kind: ClusterKind,
  observation: PendingObservation,
): Promise<ProcessorResult> {
  const tables = CLUSTER_KINDS[kind];
  try {
    if (isUnloadAltitude(observation.world_y)) {
      await db.prepare(`UPDATE ${tables.observations} SET processed = 1 WHERE id = ?`)
        .bind(observation.id)
        .run();
      return { scanned: 1, assigned: 0, failed: 0, newlyAccepted: 0 };
    }

    const candidateId = observation.linked_candidate ?? await findOrCreateCandidate(db, tables, observation);
    const newlyAccepted = await assignObservation(db, kind, candidateId, observation);
    return {
      scanned: 1,
      assigned: 1,
      failed: 0,
      newlyAccepted: newlyAccepted ? 1 : 0,
    };
  } catch (error) {
    console.error(`Failed to process ${tables.label} ${observation.id}`, error);
    return { scanned: 1, assigned: 0, failed: 1, newlyAccepted: 0 };
  }
}

async function findOrCreateCandidate(
  db: D1Database,
  tables: ClusterTables,
  observation: PendingObservation,
): Promise<number> {
  const keyValues = tables.keys.map(key => observation[key]);
  const keyParams = tables.keys.map((_, i) => `?${7 + i}`);
  const keyMatch = tables.keys.map((key, i) => `AND ${key} = ?${7 + i}`).join(" ");
  const distance = `
    (centroid_x - ?2) * (centroid_x - ?2)
    + (centroid_y - ?3) * (centroid_y - ?3)
    + (centroid_z - ?4) * (centroid_z - ?4)`;
  const nearby = `
    FROM ${tables.candidates}
    WHERE territory_id = ?1
      ${keyMatch}
      AND status != 'rejected'
      AND ${distance} <= ?6`;
  const params = [
    observation.territory_id,
    observation.world_x,
    observation.world_y,
    observation.world_z,
    observation.observed_at_utc,
    CLUSTER_RADIUS_SQUARED,
    ...keyValues,
  ];

  // One statement, so two reports of the same new spot cannot each create a candidate.
  const inserted = await db.prepare(`
    INSERT INTO ${tables.candidates} (
      territory_id, ${tables.keys.map(key => `${key},`).join(" ")}
      centroid_x, centroid_y, centroid_z,
      first_observed_at_utc, last_observed_at_utc
    )
    SELECT ?1, ${keyParams.map(param => `${param},`).join(" ")} ?2, ?3, ?4, ?5, ?5
    WHERE NOT EXISTS (SELECT 1 ${nearby})
  `).bind(...params).run();

  if (!inserted.success) {
    throw new Error("Candidate insert failed.");
  }

  const nearest = await db.prepare(`
    SELECT id
    ${nearby}
    ORDER BY ${distance}, id
    LIMIT 1
  `).bind(...params).first<{ id: number }>();

  if (nearest === null) {
    throw new Error("Candidate missing after insert.");
  }

  return nearest.id;
}

async function assignObservation(
  db: D1Database,
  kind: ClusterKind,
  candidateId: number,
  observation: PendingObservation,
): Promise<boolean> {
  const tables = CLUSTER_KINDS[kind];
  // Link, count and mark in one batch so a report is never linked without being counted.
  const counts = `
    EXISTS (SELECT 1 FROM ${tables.members} WHERE observation_id = ?10 AND candidate_id = ?2)
    AND (SELECT processed FROM ${tables.observations} WHERE id = ?10) = 0`;
  const params = [
    kind,
    candidateId,
    observation.reporter,
    observation.world_x,
    observation.world_y,
    observation.world_z,
    REVIEW_REPORTER_THRESHOLD,
    observation.observed_at_utc,
    AUTO_ACCEPT_NOTE,
    observation.id,
    observation.linked_candidate === null ? 0 : 1,
  ];

  const results = await db.batch([
    db.prepare(`
      INSERT OR IGNORE INTO ${tables.members} (candidate_id, observation_id, installation_hash)
      VALUES (?, ?, ?)
    `).bind(candidateId, observation.id, observation.installation_hash),
    db.prepare(`SELECT status FROM ${tables.candidates} WHERE id = ?`).bind(candidateId),
    db.prepare(`
      WITH delta AS (
        SELECT IIF(
          EXISTS (
            SELECT 1
            FROM candidate_reporters
            WHERE kind = ?1
              AND candidate_id = ?2
              AND reporter_hash = ?3
          )
          -- Migration 0014 put reporters of linked-but-uncounted rows in the ledger, so those rows need a counted twin.
          AND (?11 = 0 OR EXISTS (
            SELECT 1
            FROM ${tables.members} m
            JOIN ${tables.observations} o ON o.id = m.observation_id
            WHERE m.candidate_id = ?2
              AND m.observation_id <> ?10
              AND o.processed = 1
              AND COALESCE(o.reporter_hash, o.installation_hash) = ?3
          )),
          0,
          1
        ) AS distinct_delta
      ),
      reached AS (
        SELECT status = 'pending'
          AND distinct_installation_count + (SELECT distinct_delta FROM delta) >= ?7 AS accept
        FROM ${tables.candidates}
        WHERE id = ?2
      )
      UPDATE ${tables.candidates}
      SET observation_count = observation_count + 1,
        distinct_installation_count = distinct_installation_count
          + (SELECT distinct_delta FROM delta),
        centroid_x = (centroid_x * observation_count + ?4) / (observation_count + 1),
        centroid_y = (centroid_y * observation_count + ?5) / (observation_count + 1),
        centroid_z = (centroid_z * observation_count + ?6) / (observation_count + 1),
        first_observed_at_utc = MIN(first_observed_at_utc, ?8),
        last_observed_at_utc = MAX(last_observed_at_utc, ?8),
        status = IIF((SELECT accept FROM reached), 'accepted', status),
        acceptance_method = IIF((SELECT accept FROM reached), 'automatic', acceptance_method),
        reviewed_at_utc = IIF((SELECT accept FROM reached), COALESCE(reviewed_at_utc, CURRENT_TIMESTAMP), reviewed_at_utc),
        review_note = IIF((SELECT accept FROM reached), ?9, review_note),
        updated_at_utc = CURRENT_TIMESTAMP
      WHERE id = ?2 AND ${counts}
      RETURNING status
    `).bind(...params),
    db.prepare(`
      INSERT OR IGNORE INTO candidate_reporters (kind, candidate_id, reporter_hash)
      SELECT ?1, ?2, ?3
      WHERE ${counts}
    `).bind(...params.slice(0, 10)),
    db.prepare(`UPDATE ${tables.observations} SET processed = 1 WHERE id = ?`).bind(observation.id),
  ]);

  const previous = results[1].results[0] as { status: string } | undefined;
  if (previous === undefined) {
    throw new Error("Candidate missing during assign.");
  }

  const next = results[2].results[0] as { status: string } | undefined;
  return next !== undefined && previous.status === "pending" && next.status === "accepted";
}
