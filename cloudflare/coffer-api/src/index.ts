import {
  CATALOG_CACHE_TTL_SECONDS,
  invalidateAcceptedCatalogCaches,
  readCatalogCache,
  rememberCatalogPayload,
  writeCatalogCache,
} from "./catalogCache";
import {
  invalidatePotHitCache,
  POT_HIT_CACHE_TTL_SECONDS,
  readPotHitCache,
  rememberPotHitPayload,
  writePotHitCache,
} from "./potCycleCache";
import {
  processObservationById,
  processPendingObservations,
} from "./processor";
import { reporterHash } from "./reporter";
import {
  CARROT_OBJECT_BASE_ID,
  INSTANCE_KEY_PATTERN,
  OCCULT_CRESCENT_TERRITORY_IDS,
  POT_CYCLE_MAX_AGE_SECONDS,
  type CarrotLocationRequest,
  type CofferObservationRequest,
  type PotChestLocationRequest,
  type PotCycleRequest,
  validateCarrotLocation,
  validateObservation,
  validatePotChestLocation,
  validatePotCycle,
} from "./validation";
import { MIN_VALID_WORLD_Y } from "./worldBounds";

const MAX_REQUEST_BYTES = 8 * 1024;
const CANDIDATE_STATUSES = new Set(["pending", "review", "accepted", "rejected"]);
/** Drop pot_cycles older than this (seconds). Must exceed GET max age. */
const POT_CYCLE_RETAIN_SECONDS = 2 * 60 * 60;
/** Drop processed coffer/carrot rows older than this (days). Candidates keep last centroid. */
const PROCESSED_OBSERVATION_RETAIN_DAYS = 14;
/** Paid D1: prune in large batches until caught up (was capped for free-tier write budget). */
const POT_CYCLE_PRUNE_BATCH = 25_000;
const POT_CYCLE_PRUNE_MAX_ROUNDS = 20;

function jsonResponse(body: unknown, status = 200, extraHeaders: Record<string, string> = {}): Response {
  return Response.json(body, {
    status,
    headers: {
      "Cache-Control": "no-store",
      ...extraHeaders,
    },
  });
}

async function enforceRateLimit(
  request: Request,
  limiter: RateLimit,
  errorMessage = "Rate limit exceeded.",
): Promise<Response | null> {
  const key = request.headers.get("CF-Connecting-IP") ?? "local-development";
  const result = await limiter.limit({ key });
  if (result.success) {
    return null;
  }

  return jsonResponse(
    { accepted: false, found: false, error: errorMessage },
    429,
    { "Retry-After": "60" },
  );
}

async function enforceObservationRateLimit(request: Request, env: Env): Promise<Response | null> {
  return enforceRateLimit(request, env.OBSERVATION_IP_LIMITER);
}

async function enforcePotCycleRateLimit(request: Request, env: Env): Promise<Response | null> {
  return enforceRateLimit(request, env.POT_CYCLE_IP_LIMITER);
}

function authorizeAdmin(request: Request, env: Env): Response | null {
  const configuredToken = env.ADMIN_TOKEN?.trim();
  if (!configuredToken) {
    return jsonResponse({ error: "Not found." }, 404);
  }

  if (request.headers.get("Authorization") !== `Bearer ${configuredToken}`) {
    return jsonResponse(
      { error: "Unauthorized." },
      401,
      { "WWW-Authenticate": "Bearer" },
    );
  }

  return null;
}

function parsePositiveInteger(value: string | null): number | null {
  if (value === null || !/^\d+$/.test(value)) {
    return null;
  }

  const parsed = Number(value);
  return Number.isSafeInteger(parsed) && parsed > 0 ? parsed : null;
}

function parseNonNegativeInteger(value: string | null): number | null {
  if (value === null || !/^\d+$/.test(value)) {
    return null;
  }

  const parsed = Number(value);
  return Number.isSafeInteger(parsed) && parsed >= 0 ? parsed : null;
}

interface CandidateKind {
  candidatesTable: string;
  membersTable: string;
  observationsTable: string;
  candidateKeys: string;
  memberColumns: string;
  acceptedOrder: string;
  acceptsDataId: boolean;
  responseArray: "candidates" | "locations";
  exportFilename: string;
  cacheKind: "coffers" | "carrots" | "pot-chests";
  jsonKeys: (candidate: { data_id: number; pot_fate_id: number; is_reroll: number }) => object;
}

const CANDIDATE_KINDS: Record<"coffers" | "carrots" | "potChests", CandidateKind> = {
  coffers: {
    candidatesTable: "observation_candidates",
    membersTable: "observation_candidate_members",
    observationsTable: "observations",
    candidateKeys: "id, territory_id, data_id",
    memberColumns: "o.id AS observation_id, o.territory_id, o.data_id, o.world_x, o.world_y, o.world_z, o.coffer_type, o.plugin_version, o.game_version, o.observed_at_utc, o.received_at_utc",
    acceptedOrder: "territory_id, data_id, centroid_x, centroid_y, centroid_z, id",
    responseArray: "candidates",
    exportFilename: "accepted-candidates.json",
    cacheKind: "coffers",
    acceptsDataId: true,
    jsonKeys: candidate => ({ dataId: candidate.data_id }),
  },
  carrots: {
    candidatesTable: "carrot_candidates",
    membersTable: "carrot_candidate_members",
    observationsTable: "carrot_observations",
    candidateKeys: "id, territory_id",
    memberColumns: "o.id AS observation_id, o.territory_id, o.object_base_id, o.world_x, o.world_y, o.world_z, o.plugin_version, o.observed_at_utc, o.received_at_utc",
    acceptedOrder: "territory_id, centroid_x, centroid_y, centroid_z, id",
    responseArray: "locations",
    exportFilename: "accepted-carrot-locations.json",
    cacheKind: "carrots",
    acceptsDataId: false,
    jsonKeys: () => ({}),
  },
  potChests: {
    candidatesTable: "pot_chest_candidates",
    membersTable: "pot_chest_candidate_members",
    observationsTable: "pot_chest_observations",
    candidateKeys: "id, territory_id, pot_fate_id, is_reroll",
    memberColumns: "o.id AS observation_id, o.territory_id, o.pot_fate_id, o.is_reroll, o.world_x, o.world_y, o.world_z, o.plugin_version, o.observed_at_utc, o.received_at_utc",
    acceptedOrder: "territory_id, pot_fate_id, is_reroll, centroid_x, centroid_y, centroid_z, id",
    responseArray: "locations",
    exportFilename: "accepted-pot-chest-locations.json",
    cacheKind: "pot-chests",
    acceptsDataId: false,
    jsonKeys: candidate => ({ potFateId: candidate.pot_fate_id, isReroll: candidate.is_reroll !== 0 }),
  },
};

async function listCandidates(request: Request, env: Env, kind: CandidateKind): Promise<Response> {
  const url = new URL(request.url);
  const status = url.searchParams.get("status");
  if (status !== null && !CANDIDATE_STATUSES.has(status)) {
    return jsonResponse({ error: "Invalid candidate status." }, 400);
  }

  const territoryId = url.searchParams.get("territoryId");
  const parsedTerritoryId = territoryId === null ? null : parsePositiveInteger(territoryId);
  if (territoryId !== null && parsedTerritoryId === null) {
    return jsonResponse({ error: "Invalid territoryId." }, 400);
  }

  const dataId = kind.acceptsDataId ? url.searchParams.get("dataId") : null;
  const parsedDataId = dataId === null ? null : parsePositiveInteger(dataId);
  if (dataId !== null && parsedDataId === null) {
    return jsonResponse({ error: "Invalid dataId." }, 400);
  }

  const requestedLimit = url.searchParams.get("limit");
  const parsedLimit = requestedLimit === null ? 50 : parsePositiveInteger(requestedLimit);
  if (parsedLimit === null) {
    return jsonResponse({ error: "Invalid limit." }, 400);
  }

  const requestedOffset = url.searchParams.get("offset");
  const parsedOffset = requestedOffset === null ? 0 : parseNonNegativeInteger(requestedOffset);
  if (parsedOffset === null) {
    return jsonResponse({ error: "Invalid offset." }, 400);
  }

  const clauses = ["1 = 1"];
  const values: (string | number)[] = [];
  if (status !== null) {
    clauses.push("status = ?");
    values.push(status);
  }
  if (parsedTerritoryId !== null) {
    clauses.push("territory_id = ?");
    values.push(parsedTerritoryId);
  }
  if (parsedDataId !== null) {
    clauses.push("data_id = ?");
    values.push(parsedDataId);
  }

  const candidates = await env.DB.prepare(`
    SELECT ${kind.candidateKeys},
      centroid_x, centroid_y, centroid_z,
      observation_count, distinct_installation_count,
      first_observed_at_utc, last_observed_at_utc,
      status, created_at_utc, updated_at_utc,
      reviewed_at_utc, review_note, acceptance_method
    FROM ${kind.candidatesTable}
    WHERE ${clauses.join(" AND ")}
    ORDER BY updated_at_utc DESC, id DESC
    LIMIT ? OFFSET ?
  `).bind(...values, Math.min(parsedLimit, 100) + 1, parsedOffset).all();

  const pageSize = Math.min(parsedLimit, 100);
  return jsonResponse({
    candidates: candidates.results.slice(0, pageSize),
    hasMore: candidates.results.length > pageSize,
  });
}

async function getCandidateDetail(candidateId: number, env: Env, kind: CandidateKind): Promise<Response> {
  const candidate = await env.DB.prepare(`
    SELECT ${kind.candidateKeys},
      centroid_x, centroid_y, centroid_z,
      observation_count, distinct_installation_count,
      first_observed_at_utc, last_observed_at_utc,
      status, created_at_utc, updated_at_utc,
      reviewed_at_utc, review_note, acceptance_method
    FROM ${kind.candidatesTable}
    WHERE id = ?
  `).bind(candidateId).first();

  if (candidate === null) {
    return jsonResponse({ error: "Candidate not found." }, 404);
  }

  const members = await env.DB.prepare(`
    SELECT ${kind.memberColumns}
    FROM ${kind.membersTable} m
    JOIN ${kind.observationsTable} o ON o.id = m.observation_id
    WHERE m.candidate_id = ?
    ORDER BY o.observed_at_utc, o.id
  `).bind(candidateId).all();

  return jsonResponse({ candidate, members: members.results });
}

async function buildAcceptedCandidatesPayload(request: Request, env: Env, kind: CandidateKind, compact = false) {
  const url = new URL(request.url);
  const territoryId = url.searchParams.get("territoryId");
  const parsedTerritoryId = territoryId === null ? null : parsePositiveInteger(territoryId);
  if (territoryId !== null && parsedTerritoryId === null) {
    throw jsonResponse({ error: "Invalid territoryId." }, 400);
  }

  if (parsedTerritoryId !== null && !OCCULT_CRESCENT_TERRITORY_IDS.has(parsedTerritoryId)) {
    throw jsonResponse({ error: "territoryId must be an Occult Crescent zone." }, 400);
  }

  const dataId = compact || !kind.acceptsDataId ? null : url.searchParams.get("dataId");
  const parsedDataId = dataId === null ? null : parsePositiveInteger(dataId);
  if (dataId !== null && parsedDataId === null) {
    throw jsonResponse({ error: "Invalid dataId." }, 400);
  }

  const clauses = ["status = 'accepted'", "centroid_y >= ?"];
  const values: number[] = [MIN_VALID_WORLD_Y];
  if (parsedTerritoryId !== null) {
    clauses.push("territory_id = ?");
    values.push(parsedTerritoryId);
  }
  if (parsedDataId !== null) {
    clauses.push("data_id = ?");
    values.push(parsedDataId);
  }

  const columns = compact
    ? `${kind.candidateKeys},
      centroid_x, centroid_y, centroid_z`
    : `${kind.candidateKeys},
      centroid_x, centroid_y, centroid_z,
      observation_count, distinct_installation_count,
      first_observed_at_utc, last_observed_at_utc,
      acceptance_method`;

  const candidates = await env.DB.prepare(`
    SELECT ${columns}
    FROM ${kind.candidatesTable}
    WHERE ${clauses.join(" AND ")}
    ORDER BY ${kind.acceptedOrder}
  `).bind(...values).all<{
    id: number;
    territory_id: number;
    data_id: number;
    pot_fate_id: number;
    is_reroll: number;
    centroid_x: number;
    centroid_y: number;
    centroid_z: number;
    observation_count: number;
    distinct_installation_count: number;
    first_observed_at_utc: string;
    last_observed_at_utc: string;
    acceptance_method: "automatic" | "manual" | null;
  }>();

  return {
    schemaVersion: 1,
    generatedAtUtc: new Date().toISOString(),
    [kind.responseArray]: candidates.results.map(candidate => compact
      ? {
        candidateId: candidate.id,
        territoryId: candidate.territory_id,
        ...kind.jsonKeys(candidate),
        position: {
          x: candidate.centroid_x,
          y: candidate.centroid_y,
          z: candidate.centroid_z,
        },
      }
      : {
        candidateId: candidate.id,
        territoryId: candidate.territory_id,
        ...kind.jsonKeys(candidate),
        position: {
          x: candidate.centroid_x,
          y: candidate.centroid_y,
          z: candidate.centroid_z,
        },
        observationCount: candidate.observation_count,
        distinctInstallationCount: candidate.distinct_installation_count,
        firstObservedAtUtc: candidate.first_observed_at_utc,
        lastObservedAtUtc: candidate.last_observed_at_utc,
        acceptanceMethod: candidate.acceptance_method,
      }),
  };
}

async function exportAcceptedCandidates(request: Request, env: Env, kind: CandidateKind): Promise<Response> {
  try {
    const payload = await buildAcceptedCandidatesPayload(request, env, kind);
    return jsonResponse(
      payload,
      200,
      { "Content-Disposition": `attachment; filename="${kind.exportFilename}"` },
    );
  } catch (error) {
    if (error instanceof Response) {
      return error;
    }

    throw error;
  }
}

async function listAcceptedCandidatesPublic(
  request: Request,
  env: Env,
  ctx: ExecutionContext,
  kind: CandidateKind,
): Promise<Response> {
  try {
    const url = new URL(request.url);
    const cached = await readCatalogCache(kind.cacheKind, url);
    if (cached !== undefined) {
      return cached;
    }

    const payload = await buildAcceptedCandidatesPayload(request, env, kind, true);
    rememberCatalogPayload(kind.cacheKind, url, payload);
    const response = jsonResponse(payload, 200, {
      "Cache-Control": `public, max-age=${CATALOG_CACHE_TTL_SECONDS}`,
    });
    ctx.waitUntil(writeCatalogCache(kind.cacheKind, url, response).catch(error => {
      console.error(error);
    }));
    return response;
  } catch (error) {
    if (error instanceof Response) {
      return error;
    }

    throw error;
  }
}

async function reviewCandidate(request: Request, candidateId: number, env: Env, kind: CandidateKind): Promise<Response> {
  const body = await parseJsonBody(request);
  if (body === null || typeof body !== "object" || Array.isArray(body)) {
    return jsonResponse({ error: "Body must be a JSON object." }, 400);
  }

  const input = body as { status?: unknown; note?: unknown };
  if (typeof input.status !== "string" || !CANDIDATE_STATUSES.has(input.status)) {
    return jsonResponse({ error: "Invalid candidate status." }, 400);
  }

  if (input.note !== undefined
    && input.note !== null
    && (typeof input.note !== "string" || input.note.length > 512)) {
    return jsonResponse({ error: "Invalid review note." }, 400);
  }

  const existing = await env.DB.prepare(
    `SELECT id FROM ${kind.candidatesTable} WHERE id = ?`,
  ).bind(candidateId).first();
  if (existing === null) {
    return jsonResponse({ error: "Candidate not found." }, 404);
  }

  const reviewTimestamp = input.status === "accepted" || input.status === "rejected"
    ? new Date().toISOString()
    : null;
  await env.DB.prepare(`
    UPDATE ${kind.candidatesTable}
    SET status = ?,
      review_note = ?,
      reviewed_at_utc = ?,
      acceptance_method = ?,
      updated_at_utc = CURRENT_TIMESTAMP
    WHERE id = ?
  `).bind(
    input.status,
    typeof input.note === "string" && input.note.trim().length > 0 ? input.note.trim() : null,
    reviewTimestamp,
    input.status === "accepted" || input.status === "rejected" ? "manual" : null,
    candidateId,
  ).run();

  await invalidateAcceptedCatalogCaches();
  return getCandidateDetail(candidateId, env, kind);
}

async function readBodyWithinLimit(request: Request): Promise<string> {
  const contentLength = request.headers.get("Content-Length");
  if (contentLength !== null && Number(contentLength) > MAX_REQUEST_BYTES) {
    throw new Response("Request body is too large.", { status: 413 });
  }

  if (!request.body) {
    return "";
  }

  const reader = request.body.getReader();
  const decoder = new TextDecoder();
  const chunks: string[] = [];
  let byteLength = 0;

  try {
    while (true) {
      const { done, value } = await reader.read();
      if (done) {
        break;
      }

      byteLength += value.byteLength;
      if (byteLength > MAX_REQUEST_BYTES) {
        await reader.cancel();
        throw new Response("Request body is too large.", { status: 413 });
      }

      chunks.push(decoder.decode(value, { stream: true }));
    }
  } finally {
    reader.releaseLock();
  }

  chunks.push(decoder.decode());
  return chunks.join("");
}

async function parseJsonBody(request: Request): Promise<unknown> {
  const contentType = request.headers.get("Content-Type") ?? "";
  const mediaType = contentType.split(";", 1)[0].trim().toLowerCase();
  if (mediaType !== "application/json") {
    throw new Response("Content-Type must be application/json.", { status: 415 });
  }

  const bodyText = await readBodyWithinLimit(request);
  try {
    return JSON.parse(bodyText);
  } catch {
    throw new Response("Invalid JSON.", { status: 400 });
  }
}

async function submitPotCycle(request: Request, env: Env, ctx: ExecutionContext): Promise<Response> {
  const body = await parseJsonBody(request);
  const validationError = validatePotCycle(body);
  if (validationError !== null) {
    return jsonResponse({ accepted: false, error: validationError }, 400);
  }

  const pot = body as PotCycleRequest;
  const observedAtUtc = new Date(pot.observedAtUtc).toISOString();
  const instanceKey = pot.instanceKey.trim().toUpperCase();

  const result = await env.DB.prepare(`
    INSERT OR IGNORE INTO pot_cycles (
      instance_key, territory_id, datacenter_id, pot_fate_id, spawn_at_unix,
      installation_hash, plugin_version, observed_at_utc
    )
    VALUES (?, ?, ?, ?, ?, ?, ?, ?)
  `).bind(
    instanceKey,
    pot.territoryId,
    pot.datacenterId,
    pot.potFateId,
    pot.spawnAtUnix,
    pot.installationHash.trim(),
    pot.pluginVersion.trim(),
    observedAtUtc,
  ).run();

  if (!result.success) {
    return jsonResponse({ accepted: false, error: "Database insert failed." }, 500);
  }

  if (result.meta.changes === 0) {
    return jsonResponse({ accepted: true, duplicate: true });
  }

  ctx.waitUntil(invalidatePotHitCache(instanceKey).catch(error => {
    console.error(error);
  }));

  return jsonResponse({
    accepted: true,
    duplicate: false,
    potCycleId: result.meta.last_row_id,
  }, 201);
}

async function getPotCycle(request: Request, env: Env, ctx: ExecutionContext): Promise<Response> {
  const url = new URL(request.url);
  const instanceKey = url.searchParams.get("instanceKey")?.trim() ?? "";
  if (!INSTANCE_KEY_PATTERN.test(instanceKey)) {
    return jsonResponse({ found: false, error: "instanceKey must be a 64-character hex SHA-256 digest." }, 400);
  }

  const cached = await readPotHitCache(instanceKey);
  if (cached !== undefined) {
    return cached;
  }

  const minSpawnUnix = Math.floor(Date.now() / 1000) - POT_CYCLE_MAX_AGE_SECONDS;
  const row = await env.DB.prepare(`
    SELECT instance_key, territory_id, datacenter_id, pot_fate_id, spawn_at_unix, observed_at_utc
    FROM pot_cycles
    WHERE instance_key = ?
      AND spawn_at_unix >= ?
    ORDER BY spawn_at_unix DESC, id DESC
    LIMIT 1
  `).bind(instanceKey.toUpperCase(), minSpawnUnix).first<{
    instance_key: string;
    territory_id: number;
    datacenter_id: number;
    pot_fate_id: number;
    spawn_at_unix: number;
    observed_at_utc: string;
  }>();

  if (row === null) {
    return jsonResponse({ found: false });
  }

  const payload = {
    found: true,
    instanceKey: row.instance_key,
    territoryId: row.territory_id,
    datacenterId: row.datacenter_id,
    potFateId: row.pot_fate_id,
    spawnAtUnix: row.spawn_at_unix,
    observedAtUtc: row.observed_at_utc,
  };
  rememberPotHitPayload(instanceKey, payload);
  const response = jsonResponse(payload, 200, {
    "Cache-Control": `public, max-age=${POT_HIT_CACHE_TTL_SECONDS}`,
  });
  ctx.waitUntil(writePotHitCache(instanceKey, response).catch(error => {
    console.error(error);
  }));
  return response;
}

async function submitCarrotLocation(
  request: Request,
  env: Env,
  reporter: string | null,
): Promise<{ response: Response; observationId: number | null }> {
  const body = await parseJsonBody(request);
  const validationError = validateCarrotLocation(body);
  if (validationError !== null) {
    return { response: jsonResponse({ accepted: false, error: validationError }, 400), observationId: null };
  }

  const observation = body as CarrotLocationRequest;
  const observedAtUtc = new Date(observation.observedAtUtc).toISOString();
  const result = await env.DB.prepare(`
    INSERT INTO carrot_observations (
      territory_id, world_x, world_y, world_z,
      object_base_id, installation_hash, reporter_hash, plugin_version, observed_at_utc
    )
    SELECT ?, ?, ?, ?, ?, ?, ?, ?, ?
    WHERE NOT EXISTS (
      SELECT 1
      FROM carrot_observations
      WHERE installation_hash = ?
        AND territory_id = ?
        AND ABS(world_x - ?) <= 0.1
        AND ABS(world_y - ?) <= 0.1
        AND ABS(world_z - ?) <= 0.1
        AND received_at_utc >= datetime('now', '-10 minutes')
    )
  `).bind(
    observation.territoryId,
    observation.worldX,
    observation.worldY,
    observation.worldZ,
    CARROT_OBJECT_BASE_ID,
    observation.installationHash.trim(),
    reporter,
    observation.pluginVersion.trim(),
    observedAtUtc,
    observation.installationHash.trim(),
    observation.territoryId,
    observation.worldX,
    observation.worldY,
    observation.worldZ,
  ).run();

  if (!result.success) {
    return {
      response: jsonResponse({ accepted: false, error: "Database insert failed." }, 500),
      observationId: null,
    };
  }

  if (result.meta.changes === 0) {
    return { response: jsonResponse({ accepted: true, duplicate: true }), observationId: null };
  }

  const observationId = result.meta.last_row_id ?? null;
  return {
    response: jsonResponse({
      accepted: true,
      duplicate: false,
      observationId,
    }, 201),
    observationId,
  };
}

async function submitObservation(
  request: Request,
  env: Env,
  reporter: string | null,
): Promise<{ response: Response; observationId: number | null }> {
  const body = await parseJsonBody(request);
  const validationError = validateObservation(body);
  if (validationError !== null) {
    return { response: jsonResponse({ accepted: false, error: validationError }, 400), observationId: null };
  }

  const observation = body as CofferObservationRequest;
  const observedAtUtc = new Date(observation.observedAtUtc).toISOString();
  const result = await env.DB.prepare(`
    INSERT INTO observations (
      territory_id, data_id, map_id, world_x, world_y, world_z,
      coffer_type, installation_hash, reporter_hash, plugin_version,
      game_version, observed_at_utc
    )
    SELECT ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?
    WHERE NOT EXISTS (
      SELECT 1
      FROM observations
      WHERE installation_hash = ?
        AND territory_id = ?
        AND ABS(world_x - ?) <= 0.1
        AND ABS(world_y - ?) <= 0.1
        AND ABS(world_z - ?) <= 0.1
        AND received_at_utc >= datetime('now', '-10 minutes')
    )
  `).bind(
    observation.territoryId,
    observation.dataId ?? null,
    observation.mapId ?? null,
    observation.worldX,
    observation.worldY,
    observation.worldZ,
    observation.cofferType?.trim() || null,
    observation.installationHash.trim(),
    reporter,
    observation.pluginVersion.trim(),
    observation.gameVersion?.trim() || null,
    observedAtUtc,
    observation.installationHash.trim(),
    observation.territoryId,
    observation.worldX,
    observation.worldY,
    observation.worldZ,
  ).run();

  if (!result.success) {
    return {
      response: jsonResponse({ accepted: false, error: "Database insert failed." }, 500),
      observationId: null,
    };
  }

  if (result.meta.changes === 0) {
    return { response: jsonResponse({ accepted: true, duplicate: true }), observationId: null };
  }

  const observationId = result.meta.last_row_id ?? null;
  return {
    response: jsonResponse({
      accepted: true,
      duplicate: false,
      observationId,
    }, 201),
    observationId,
  };
}

async function submitPotChestLocation(
  request: Request,
  env: Env,
  reporter: string | null,
): Promise<{ response: Response; observationId: number | null }> {
  const body = await parseJsonBody(request);
  const validationError = validatePotChestLocation(body);
  if (validationError !== null) {
    return { response: jsonResponse({ accepted: false, error: validationError }, 400), observationId: null };
  }

  const observation = body as PotChestLocationRequest;
  const observedAtUtc = new Date(observation.observedAtUtc).toISOString();
  const isRerollInt = observation.isReroll ? 1 : 0;
  const result = await env.DB.prepare(`
    INSERT INTO pot_chest_observations (
      territory_id, pot_fate_id, is_reroll, world_x, world_y, world_z,
      installation_hash, reporter_hash, plugin_version, observed_at_utc
    )
    SELECT ?, ?, ?, ?, ?, ?, ?, ?, ?, ?
    WHERE NOT EXISTS (
      SELECT 1
      FROM pot_chest_observations
      WHERE installation_hash = ?
        AND territory_id = ?
        AND pot_fate_id = ?
        AND is_reroll = ?
        AND ABS(world_x - ?) <= 0.1
        AND ABS(world_y - ?) <= 0.1
        AND ABS(world_z - ?) <= 0.1
        AND received_at_utc >= datetime('now', '-10 minutes')
    )
  `).bind(
    observation.territoryId,
    observation.potFateId,
    isRerollInt,
    observation.worldX,
    observation.worldY,
    observation.worldZ,
    observation.installationHash.trim(),
    reporter,
    observation.pluginVersion.trim(),
    observedAtUtc,
    observation.installationHash.trim(),
    observation.territoryId,
    observation.potFateId,
    isRerollInt,
    observation.worldX,
    observation.worldY,
    observation.worldZ,
  ).run();

  if (!result.success) {
    return {
      response: jsonResponse({ accepted: false, error: "Database insert failed." }, 500),
      observationId: null,
    };
  }

  if (result.meta.changes === 0) {
    return { response: jsonResponse({ accepted: true, duplicate: true }), observationId: null };
  }

  const observationId = result.meta.last_row_id ?? null;
  return {
    response: jsonResponse({
      accepted: true,
      duplicate: false,
      observationId,
    }, 201),
    observationId,
  };
}

export default {
  async fetch(request: Request, env: Env, ctx: ExecutionContext): Promise<Response> {
    const url = new URL(request.url);

    if (request.method === "GET" && url.pathname === "/health") {
      return jsonResponse({ status: "ok" });
    }

    if (request.method === "GET" && url.pathname === "/api/v1/candidates") {
      try {
        return await listAcceptedCandidatesPublic(request, env, ctx, CANDIDATE_KINDS.coffers);
      } catch (error) {
        if (error instanceof Response) {
          return error;
        }

        console.error(error);
        return jsonResponse({ error: "Unexpected server error." }, 500);
      }
    }

    if (request.method === "GET" && url.pathname === "/api/v1/carrot-locations") {
      try {
        return await listAcceptedCandidatesPublic(request, env, ctx, CANDIDATE_KINDS.carrots);
      } catch (error) {
        if (error instanceof Response) {
          return error;
        }

        console.error(error);
        return jsonResponse({ error: "Unexpected server error." }, 500);
      }
    }

    if (request.method === "GET" && url.pathname === "/api/v1/pot-chest-locations") {
      try {
        return await listAcceptedCandidatesPublic(request, env, ctx, CANDIDATE_KINDS.potChests);
      } catch (error) {
        if (error instanceof Response) {
          return error;
        }

        console.error(error);
        return jsonResponse({ error: "Unexpected server error." }, 500);
      }
    }

    if (url.pathname.startsWith("/api/v1/admin/")) {
      const authorizationError = authorizeAdmin(request, env);
      if (authorizationError !== null) {
        return authorizationError;
      }

      try {
        if (request.method === "GET" && url.pathname === "/api/v1/admin/candidates") {
          return await listCandidates(request, env, CANDIDATE_KINDS.coffers);
        }

        if (request.method === "GET" && url.pathname === "/api/v1/admin/export/accepted-candidates") {
          return await exportAcceptedCandidates(request, env, CANDIDATE_KINDS.coffers);
        }

        if (request.method === "GET" && url.pathname === "/api/v1/admin/carrot-candidates") {
          return await listCandidates(request, env, CANDIDATE_KINDS.carrots);
        }

        if (request.method === "GET" && url.pathname === "/api/v1/admin/export/accepted-carrot-locations") {
          return await exportAcceptedCandidates(request, env, CANDIDATE_KINDS.carrots);
        }

        if (request.method === "GET" && url.pathname === "/api/v1/admin/pot-chest-candidates") {
          return await listCandidates(request, env, CANDIDATE_KINDS.potChests);
        }

        if (request.method === "GET" && url.pathname === "/api/v1/admin/export/accepted-pot-chest-locations") {
          return await exportAcceptedCandidates(request, env, CANDIDATE_KINDS.potChests);
        }

        const candidateDetailMatch = url.pathname.match(/^\/api\/v1\/admin\/candidates\/(\d+)$/);
        if (candidateDetailMatch !== null && request.method === "GET") {
          const candidateId = parsePositiveInteger(candidateDetailMatch[1]);
          return candidateId === null
            ? jsonResponse({ error: "Invalid candidate ID." }, 400)
            : await getCandidateDetail(candidateId, env, CANDIDATE_KINDS.coffers);
        }

        const candidateReviewMatch = url.pathname.match(/^\/api\/v1\/admin\/candidates\/(\d+)\/review$/);
        if (candidateReviewMatch !== null && request.method === "POST") {
          const candidateId = parsePositiveInteger(candidateReviewMatch[1]);
          return candidateId === null
            ? jsonResponse({ error: "Invalid candidate ID." }, 400)
            : await reviewCandidate(request, candidateId, env, CANDIDATE_KINDS.coffers);
        }

        const carrotDetailMatch = url.pathname.match(/^\/api\/v1\/admin\/carrot-candidates\/(\d+)$/);
        if (carrotDetailMatch !== null && request.method === "GET") {
          const candidateId = parsePositiveInteger(carrotDetailMatch[1]);
          return candidateId === null
            ? jsonResponse({ error: "Invalid candidate ID." }, 400)
            : await getCandidateDetail(candidateId, env, CANDIDATE_KINDS.carrots);
        }

        const carrotReviewMatch = url.pathname.match(/^\/api\/v1\/admin\/carrot-candidates\/(\d+)\/review$/);
        if (carrotReviewMatch !== null && request.method === "POST") {
          const candidateId = parsePositiveInteger(carrotReviewMatch[1]);
          return candidateId === null
            ? jsonResponse({ error: "Invalid candidate ID." }, 400)
            : await reviewCandidate(request, candidateId, env, CANDIDATE_KINDS.carrots);
        }

        const potChestDetailMatch = url.pathname.match(/^\/api\/v1\/admin\/pot-chest-candidates\/(\d+)$/);
        if (potChestDetailMatch !== null && request.method === "GET") {
          const candidateId = parsePositiveInteger(potChestDetailMatch[1]);
          return candidateId === null
            ? jsonResponse({ error: "Invalid candidate ID." }, 400)
            : await getCandidateDetail(candidateId, env, CANDIDATE_KINDS.potChests);
        }

        const potChestReviewMatch = url.pathname.match(/^\/api\/v1\/admin\/pot-chest-candidates\/(\d+)\/review$/);
        if (potChestReviewMatch !== null && request.method === "POST") {
          const candidateId = parsePositiveInteger(potChestReviewMatch[1]);
          return candidateId === null
            ? jsonResponse({ error: "Invalid candidate ID." }, 400)
            : await reviewCandidate(request, candidateId, env, CANDIDATE_KINDS.potChests);
        }

        return jsonResponse({ error: "Not found." }, 404);
      } catch (error) {
        if (error instanceof Response) {
          return error;
        }

        console.error(error);
        return jsonResponse({ error: "Unexpected server error." }, 500);
      }
    }

    if (request.method === "POST" && url.pathname === "/api/v1/observations") {
      try {
        const rateLimitResponse = await enforceObservationRateLimit(request, env);
        if (rateLimitResponse !== null) {
          return rateLimitResponse;
        }

        const submitted = await submitObservation(request, env, await reporterHash(request, env));
        if (submitted.observationId !== null) {
          ctx.waitUntil(processObservationById(env, "coffers", submitted.observationId).then(async result => {
            console.log("Observation processor (post-submit)", result);
            if (result.newlyAccepted > 0) {
              await invalidateAcceptedCatalogCaches();
            }
          }).catch(error => {
            console.error(error);
          }));
        }

        return submitted.response;
      } catch (error) {
        if (error instanceof Response) {
          return error;
        }

        console.error(error);
        return jsonResponse({ accepted: false, error: "Unexpected server error." }, 500);
      }
    }

    if (request.method === "POST" && url.pathname === "/api/v1/carrot-locations") {
      try {
        const rateLimitResponse = await enforceObservationRateLimit(request, env);
        if (rateLimitResponse !== null) {
          return rateLimitResponse;
        }

        const submitted = await submitCarrotLocation(request, env, await reporterHash(request, env));
        if (submitted.observationId !== null) {
          ctx.waitUntil(processObservationById(env, "carrots", submitted.observationId).then(async result => {
            console.log("Carrot processor (post-submit)", result);
            if (result.newlyAccepted > 0) {
              await invalidateAcceptedCatalogCaches();
            }
          }).catch(error => {
            console.error(error);
          }));
        }

        return submitted.response;
      } catch (error) {
        if (error instanceof Response) {
          return error;
        }

        console.error(error);
        return jsonResponse({ accepted: false, error: "Unexpected server error." }, 500);
      }
    }

    if (request.method === "POST" && url.pathname === "/api/v1/pot-chest-locations") {
      try {
        const rateLimitResponse = await enforceObservationRateLimit(request, env);
        if (rateLimitResponse !== null) {
          return rateLimitResponse;
        }

        const submitted = await submitPotChestLocation(request, env, await reporterHash(request, env));
        if (submitted.observationId !== null) {
          ctx.waitUntil(processObservationById(env, "pot-chests", submitted.observationId).then(async result => {
            console.log("Pot-chest processor (post-submit)", result);
            if (result.newlyAccepted > 0) {
              await invalidateAcceptedCatalogCaches();
            }
          }).catch(error => {
            console.error(error);
          }));
        }

        return submitted.response;
      } catch (error) {
        if (error instanceof Response) {
          return error;
        }

        console.error(error);
        return jsonResponse({ accepted: false, error: "Unexpected server error." }, 500);
      }
    }

    if (request.method === "POST" && url.pathname === "/api/v1/pot-cycles") {
      try {
        const rateLimitResponse = await enforcePotCycleRateLimit(request, env);
        if (rateLimitResponse !== null) {
          return rateLimitResponse;
        }

        return await submitPotCycle(request, env, ctx);
      } catch (error) {
        if (error instanceof Response) {
          return error;
        }

        console.error(error);
        return jsonResponse({ accepted: false, error: "Unexpected server error." }, 500);
      }
    }

    if (request.method === "GET" && url.pathname === "/api/v1/pot-cycles") {
      try {
        const rateLimitResponse = await enforcePotCycleRateLimit(request, env);
        if (rateLimitResponse !== null) {
          return rateLimitResponse;
        }

        return await getPotCycle(request, env, ctx);
      } catch (error) {
        if (error instanceof Response) {
          return error;
        }

        console.error(error);
        return jsonResponse({ found: false, error: "Unexpected server error." }, 500);
      }
    }

    return jsonResponse({ error: "Not found." }, 404);
  },

  async scheduled(_controller: ScheduledController, env: Env, _ctx: ExecutionContext): Promise<void> {
    const cofferResult = await runStep("Observation processor", () => processPendingObservations(env, "coffers"));
    const carrotResult = await runStep("Carrot processor", () => processPendingObservations(env, "carrots"));
    const potChestResult = await runStep("Pot-chest processor", () => processPendingObservations(env, "pot-chests"));
    const rejectedUnload = await runStep("Unload-altitude candidate reject", () => rejectUnloadAltitudeCandidates(env));
    if ((cofferResult?.newlyAccepted ?? 0) > 0
      || (carrotResult?.newlyAccepted ?? 0) > 0
      || (potChestResult?.newlyAccepted ?? 0) > 0
      || (rejectedUnload?.coffers ?? 0) > 0
      || (rejectedUnload?.carrots ?? 0) > 0
      || (rejectedUnload?.potChests ?? 0) > 0) {
      await runStep("Catalog cache clear", invalidateAcceptedCatalogCaches);
    }

    await runStep("Pot cycle prune", () => pruneStalePotCycles(env));
    await runStep("Processed observation prune", () => pruneProcessedObservations(env));
  },
} satisfies ExportedHandler<Env>;

async function runStep<T>(name: string, run: () => Promise<T>): Promise<T | null> {
  try {
    const result = await run();
    console.log(`${name} completed`, result);
    return result;
  } catch (error) {
    console.error(`${name} failed`, error);
    return null;
  }
}

/** Drop accepted pads whose centroid is unload / inside-floor junk (hamlet basement is ~−162). */
async function rejectUnloadAltitudeCandidates(
  env: Env,
): Promise<{ coffers: number; carrots: number; potChests: number }> {
  const coffers = await env.DB.prepare(`
    UPDATE observation_candidates
    SET status = 'rejected',
      review_note = 'Unload / inside-floor altitude (centroid_y < -250).',
      reviewed_at_utc = CURRENT_TIMESTAMP,
      updated_at_utc = CURRENT_TIMESTAMP
    WHERE status = 'accepted'
      AND centroid_y < ?
  `).bind(MIN_VALID_WORLD_Y).run();

  const carrots = await env.DB.prepare(`
    UPDATE carrot_candidates
    SET status = 'rejected',
      review_note = 'Unload / inside-floor altitude (centroid_y < -250).',
      reviewed_at_utc = CURRENT_TIMESTAMP,
      updated_at_utc = CURRENT_TIMESTAMP
    WHERE status = 'accepted'
      AND centroid_y < ?
  `).bind(MIN_VALID_WORLD_Y).run();

  const potChests = await env.DB.prepare(`
    UPDATE pot_chest_candidates
    SET status = 'rejected',
      review_note = 'Unload / inside-floor altitude (centroid_y < -250).',
      reviewed_at_utc = CURRENT_TIMESTAMP,
      updated_at_utc = CURRENT_TIMESTAMP
    WHERE status = 'accepted'
      AND centroid_y < ?
  `).bind(MIN_VALID_WORLD_Y).run();

  return {
    coffers: coffers.meta.changes ?? 0,
    carrots: carrots.meta.changes ?? 0,
    potChests: potChests.meta.changes ?? 0,
  };
}

/** Paid D1: delete stale pot_cycles in large rounds until caught up. */
async function pruneStalePotCycles(env: Env): Promise<{ deleted: number; rounds: number }> {
  const cutoffUnix = Math.floor(Date.now() / 1000) - POT_CYCLE_RETAIN_SECONDS;
  let deleted = 0;
  let rounds = 0;
  for (; rounds < POT_CYCLE_PRUNE_MAX_ROUNDS; rounds++) {
    const result = await env.DB.prepare(`
      DELETE FROM pot_cycles
      WHERE id IN (
        SELECT id
        FROM pot_cycles
        WHERE spawn_at_unix < ?
        ORDER BY id
        LIMIT ?
      )
    `).bind(cutoffUnix, POT_CYCLE_PRUNE_BATCH).run();

    const changes = result.meta.changes ?? 0;
    deleted += changes;
    if (changes < POT_CYCLE_PRUNE_BATCH) {
      rounds++;
      break;
    }
  }

  return { deleted, rounds };
}

/** Drop clustered coffer/carrot/pot-chest rows (and their member links) older than the retain window. */
async function pruneProcessedObservations(
  env: Env,
): Promise<{ coffers: number; carrots: number; potChests: number }> {
  const coffers = await pruneProcessedTable(
    env,
    "observation_candidate_members",
    "observations",
  );
  const carrots = await pruneProcessedTable(
    env,
    "carrot_candidate_members",
    "carrot_observations",
  );
  const potChests = await pruneProcessedTable(
    env,
    "pot_chest_candidate_members",
    "pot_chest_observations",
  );
  return { coffers, carrots, potChests };
}

async function pruneProcessedTable(
  env: Env,
  membersTable: "observation_candidate_members" | "carrot_candidate_members" | "pot_chest_candidate_members",
  observationsTable: "observations" | "carrot_observations" | "pot_chest_observations",
): Promise<number> {
  let deleted = 0;
  const staleIds = `
    SELECT id
    FROM ${observationsTable}
    WHERE processed = 1
      AND received_at_utc < datetime('now', '-${PROCESSED_OBSERVATION_RETAIN_DAYS} days')
    ORDER BY id
    LIMIT ?
  `;
  for (let round = 0; round < POT_CYCLE_PRUNE_MAX_ROUNDS; round++) {
    const results = await env.DB.batch([
      env.DB.prepare(`
        DELETE FROM ${membersTable}
        WHERE observation_id IN (${staleIds})
      `).bind(POT_CYCLE_PRUNE_BATCH),
      env.DB.prepare(`
        DELETE FROM ${observationsTable}
        WHERE id IN (${staleIds})
      `).bind(POT_CYCLE_PRUNE_BATCH),
    ]);

    const changes = results[1].meta.changes ?? 0;
    deleted += changes;
    if (changes < POT_CYCLE_PRUNE_BATCH) {
      break;
    }
  }

  return deleted;
}
