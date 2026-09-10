import {
  CATALOG_CACHE_TTL_SECONDS,
  invalidateAcceptedCatalogCaches,
  readCatalogCache,
  rememberCatalogPayload,
  writeCatalogCache,
} from "./catalogCache";
import {
  processCarrotObservationById,
  processPendingCarrotObservations,
} from "./carrotProcessor";
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
import { MIN_VALID_WORLD_Y, isUnloadAltitude } from "./worldBounds";

interface CofferObservationRequest {
  territoryId: number;
  dataId?: number | null;
  mapId?: number | null;
  worldX: number;
  worldY: number;
  worldZ: number;
  cofferType?: string | null;
  installationHash: string;
  pluginVersion: string;
  gameVersion?: string | null;
  observedAtUtc: string;
}

interface CarrotLocationRequest {
  territoryId: number;
  worldX: number;
  worldY: number;
  worldZ: number;
  objectBaseId?: number | null;
  installationHash: string;
  pluginVersion: string;
  observedAtUtc: string;
}

interface PotCycleRequest {
  instanceKey: string;
  territoryId: number;
  datacenterId: number;
  potFateId: number;
  spawnAtUnix: number;
  installationHash: string;
  pluginVersion: string;
  observedAtUtc: string;
}

const MAX_REQUEST_BYTES = 8 * 1024;
const MAX_STRING_LENGTH = 128;
const UTC_TIMESTAMP_PATTERN = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?(?:Z|\+00:00)$/;
const INSTANCE_KEY_PATTERN = /^[0-9A-Fa-f]{64}$/;
const CANDIDATE_STATUSES = new Set(["pending", "review", "accepted", "rejected"]);
/** South Horn (1252) and North Horn (1346). */
const OCCULT_CRESCENT_TERRITORY_IDS = new Set([1252, 1346]);
/** Persistent / Pleading pots (SH) and Daylight / Pot of Bother (NH). */
const OCCULT_POT_FATE_IDS = new Set([1976, 1977, 2072, 2073]);
/** OccultObjectType.Carrot */
const CARROT_OBJECT_BASE_ID = 2010139;
/** Only return pot anchors newer than this (seconds). */
const POT_CYCLE_MAX_AGE_SECONDS = 45 * 60;
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

async function listCandidates(request: Request, env: Env): Promise<Response> {
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

  const dataId = url.searchParams.get("dataId");
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
    SELECT id, territory_id, data_id,
      centroid_x, centroid_y, centroid_z,
      observation_count, distinct_installation_count,
      first_observed_at_utc, last_observed_at_utc,
      status, created_at_utc, updated_at_utc,
      reviewed_at_utc, review_note, acceptance_method
    FROM observation_candidates
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

async function getCandidateDetail(candidateId: number, env: Env): Promise<Response> {
  const candidate = await env.DB.prepare(`
    SELECT id, territory_id, data_id,
      centroid_x, centroid_y, centroid_z,
      observation_count, distinct_installation_count,
      first_observed_at_utc, last_observed_at_utc,
      status, created_at_utc, updated_at_utc,
      reviewed_at_utc, review_note, acceptance_method
    FROM observation_candidates
    WHERE id = ?
  `).bind(candidateId).first();

  if (candidate === null) {
    return jsonResponse({ error: "Candidate not found." }, 404);
  }

  const members = await env.DB.prepare(`
    SELECT o.id AS observation_id,
      o.territory_id, o.data_id,
      o.world_x, o.world_y, o.world_z,
      o.coffer_type, o.plugin_version,
      o.game_version, o.observed_at_utc,
      o.received_at_utc
    FROM observation_candidate_members m
    JOIN observations o ON o.id = m.observation_id
    WHERE m.candidate_id = ?
    ORDER BY o.observed_at_utc, o.id
  `).bind(candidateId).all();

  return jsonResponse({ candidate, members: members.results });
}

async function buildAcceptedCandidatesPayload(request: Request, env: Env, compact = false): Promise<{
  schemaVersion: number;
  generatedAtUtc: string;
  candidates: Array<{
    candidateId: number;
    territoryId: number;
    dataId: number;
    position: { x: number; y: number; z: number };
    observationCount?: number;
    distinctInstallationCount?: number;
    firstObservedAtUtc?: string;
    lastObservedAtUtc?: string;
    acceptanceMethod?: "automatic" | "manual" | null;
  }>;
}> {
  const url = new URL(request.url);
  const territoryId = url.searchParams.get("territoryId");
  const parsedTerritoryId = territoryId === null ? null : parsePositiveInteger(territoryId);
  if (territoryId !== null && parsedTerritoryId === null) {
    throw jsonResponse({ error: "Invalid territoryId." }, 400);
  }

  if (parsedTerritoryId !== null && !OCCULT_CRESCENT_TERRITORY_IDS.has(parsedTerritoryId)) {
    throw jsonResponse({ error: "territoryId must be an Occult Crescent zone." }, 400);
  }

  const dataId = url.searchParams.get("dataId");
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
    ? `id, territory_id, data_id,
      centroid_x, centroid_y, centroid_z`
    : `id, territory_id, data_id,
      centroid_x, centroid_y, centroid_z,
      observation_count, distinct_installation_count,
      first_observed_at_utc, last_observed_at_utc,
      acceptance_method`;

  const candidates = await env.DB.prepare(`
    SELECT ${columns}
    FROM observation_candidates
    WHERE ${clauses.join(" AND ")}
    ORDER BY territory_id, data_id,
      centroid_x, centroid_y, centroid_z, id
  `).bind(...values).all<{
    id: number;
    territory_id: number;
    data_id: number;
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
    candidates: candidates.results.map(candidate => compact
      ? {
        candidateId: candidate.id,
        territoryId: candidate.territory_id,
        dataId: candidate.data_id,
        position: {
          x: candidate.centroid_x,
          y: candidate.centroid_y,
          z: candidate.centroid_z,
        },
      }
      : {
        candidateId: candidate.id,
        territoryId: candidate.territory_id,
        dataId: candidate.data_id,
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

async function exportAcceptedCandidates(request: Request, env: Env): Promise<Response> {
  try {
    const payload = await buildAcceptedCandidatesPayload(request, env);
    return jsonResponse(
      payload,
      200,
      { "Content-Disposition": "attachment; filename=\"accepted-candidates.json\"" },
    );
  } catch (error) {
    if (error instanceof Response) {
      return error;
    }

    throw error;
  }
}

/** Public plugin catalog — accepted candidates only (no admin token). */
async function listAcceptedCandidatesPublic(
  request: Request,
  env: Env,
  ctx: ExecutionContext,
): Promise<Response> {
  try {
    const url = new URL(request.url);
    const cached = await readCatalogCache("coffers", url);
    if (cached !== undefined) {
      return cached;
    }

    const payload = await buildAcceptedCandidatesPayload(request, env, true);
    rememberCatalogPayload("coffers", url, payload);
    const response = jsonResponse(payload, 200, {
      "Cache-Control": `public, max-age=${CATALOG_CACHE_TTL_SECONDS}`,
    });
    ctx.waitUntil(writeCatalogCache("coffers", url, response).catch(error => {
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

async function listCarrotCandidates(request: Request, env: Env): Promise<Response> {
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

  const candidates = await env.DB.prepare(`
    SELECT id, territory_id,
      centroid_x, centroid_y, centroid_z,
      observation_count, distinct_installation_count,
      first_observed_at_utc, last_observed_at_utc,
      status, created_at_utc, updated_at_utc,
      reviewed_at_utc, review_note, acceptance_method
    FROM carrot_candidates
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

async function getCarrotCandidateDetail(candidateId: number, env: Env): Promise<Response> {
  const candidate = await env.DB.prepare(`
    SELECT id, territory_id,
      centroid_x, centroid_y, centroid_z,
      observation_count, distinct_installation_count,
      first_observed_at_utc, last_observed_at_utc,
      status, created_at_utc, updated_at_utc,
      reviewed_at_utc, review_note, acceptance_method
    FROM carrot_candidates
    WHERE id = ?
  `).bind(candidateId).first();

  if (candidate === null) {
    return jsonResponse({ error: "Candidate not found." }, 404);
  }

  const members = await env.DB.prepare(`
    SELECT o.id AS observation_id,
      o.territory_id, o.object_base_id,
      o.world_x, o.world_y, o.world_z,
      o.plugin_version, o.observed_at_utc, o.received_at_utc
    FROM carrot_candidate_members m
    JOIN carrot_observations o ON o.id = m.observation_id
    WHERE m.candidate_id = ?
    ORDER BY o.observed_at_utc, o.id
  `).bind(candidateId).all();

  return jsonResponse({ candidate, members: members.results });
}

async function buildAcceptedCarrotLocationsPayload(request: Request, env: Env, compact = false): Promise<{
  schemaVersion: number;
  generatedAtUtc: string;
  locations: Array<{
    candidateId: number;
    territoryId: number;
    position: { x: number; y: number; z: number };
    observationCount?: number;
    distinctInstallationCount?: number;
    firstObservedAtUtc?: string;
    lastObservedAtUtc?: string;
    acceptanceMethod?: "automatic" | "manual" | null;
  }>;
}> {
  const url = new URL(request.url);
  const territoryId = url.searchParams.get("territoryId");
  const parsedTerritoryId = territoryId === null ? null : parsePositiveInteger(territoryId);
  if (territoryId !== null && parsedTerritoryId === null) {
    throw jsonResponse({ error: "Invalid territoryId." }, 400);
  }

  if (parsedTerritoryId !== null && !OCCULT_CRESCENT_TERRITORY_IDS.has(parsedTerritoryId)) {
    throw jsonResponse({ error: "territoryId must be an Occult Crescent zone." }, 400);
  }

  const clauses = ["status = 'accepted'", "centroid_y >= ?"];
  const values: number[] = [MIN_VALID_WORLD_Y];
  if (parsedTerritoryId !== null) {
    clauses.push("territory_id = ?");
    values.push(parsedTerritoryId);
  }

  const columns = compact
    ? `id, territory_id,
      centroid_x, centroid_y, centroid_z`
    : `id, territory_id,
      centroid_x, centroid_y, centroid_z,
      observation_count, distinct_installation_count,
      first_observed_at_utc, last_observed_at_utc,
      acceptance_method`;

  const candidates = await env.DB.prepare(`
    SELECT ${columns}
    FROM carrot_candidates
    WHERE ${clauses.join(" AND ")}
    ORDER BY territory_id, centroid_x, centroid_y, centroid_z, id
  `).bind(...values).all<{
    id: number;
    territory_id: number;
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
    locations: candidates.results.map(candidate => compact
      ? {
        candidateId: candidate.id,
        territoryId: candidate.territory_id,
        position: {
          x: candidate.centroid_x,
          y: candidate.centroid_y,
          z: candidate.centroid_z,
        },
      }
      : {
        candidateId: candidate.id,
        territoryId: candidate.territory_id,
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

async function exportAcceptedCarrotLocations(request: Request, env: Env): Promise<Response> {
  try {
    const payload = await buildAcceptedCarrotLocationsPayload(request, env);
    return jsonResponse(
      payload,
      200,
      { "Content-Disposition": "attachment; filename=\"accepted-carrot-locations.json\"" },
    );
  } catch (error) {
    if (error instanceof Response) {
      return error;
    }

    throw error;
  }
}

async function listAcceptedCarrotLocationsPublic(
  request: Request,
  env: Env,
  ctx: ExecutionContext,
): Promise<Response> {
  try {
    const url = new URL(request.url);
    const cached = await readCatalogCache("carrots", url);
    if (cached !== undefined) {
      return cached;
    }

    const payload = await buildAcceptedCarrotLocationsPayload(request, env, true);
    rememberCatalogPayload("carrots", url, payload);
    const response = jsonResponse(payload, 200, {
      "Cache-Control": `public, max-age=${CATALOG_CACHE_TTL_SECONDS}`,
    });
    ctx.waitUntil(writeCatalogCache("carrots", url, response).catch(error => {
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

async function reviewCarrotCandidate(request: Request, candidateId: number, env: Env): Promise<Response> {
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
    "SELECT id FROM carrot_candidates WHERE id = ?",
  ).bind(candidateId).first();
  if (existing === null) {
    return jsonResponse({ error: "Candidate not found." }, 404);
  }

  const reviewTimestamp = input.status === "accepted" || input.status === "rejected"
    ? new Date().toISOString()
    : null;
  await env.DB.prepare(`
    UPDATE carrot_candidates
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
  return getCarrotCandidateDetail(candidateId, env);
}

async function reviewCandidate(request: Request, candidateId: number, env: Env): Promise<Response> {
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
    "SELECT id FROM observation_candidates WHERE id = ?",
  ).bind(candidateId).first();
  if (existing === null) {
    return jsonResponse({ error: "Candidate not found." }, 404);
  }

  const reviewTimestamp = input.status === "accepted" || input.status === "rejected"
    ? new Date().toISOString()
    : null;
  await env.DB.prepare(`
    UPDATE observation_candidates
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
  return getCandidateDetail(candidateId, env);
}

function isFiniteNumber(value: unknown): value is number {
  return typeof value === "number" && Number.isFinite(value);
}

function isIntegerInRange(value: unknown, minimum: number, maximum: number): value is number {
  return typeof value === "number"
    && Number.isInteger(value)
    && value >= minimum
    && value <= maximum;
}

function isAcceptableString(value: unknown, required: boolean, maxLength = MAX_STRING_LENGTH): boolean {
  if (value === null || value === undefined) {
    return !required;
  }

  return typeof value === "string"
    && value.trim().length > 0
    && value.length <= maxLength;
}

function validateObservation(value: unknown): string | null {
  if (value === null || typeof value !== "object" || Array.isArray(value)) {
    return "Body must be a JSON object.";
  }

  const observation = value as Partial<CofferObservationRequest>;
  if (!isIntegerInRange(observation.territoryId, 1, 100_000)) {
    return "Invalid territoryId.";
  }

  if (!OCCULT_CRESCENT_TERRITORY_IDS.has(observation.territoryId!)) {
    return "territoryId must be an Occult Crescent zone.";
  }

  // BOCCHI accepts any positive coffer BaseId in OC (silver/bronze hunt + pot-reveal).
  if (!isIntegerInRange(observation.dataId, 1, 4_294_967_295)) {
    return "Invalid dataId.";
  }

  if (observation.mapId !== null
    && observation.mapId !== undefined
    && !isIntegerInRange(observation.mapId, 1, 100_000)) {
    return "Invalid mapId.";
  }

  if (!isFiniteNumber(observation.worldX)
    || !isFiniteNumber(observation.worldY)
    || !isFiniteNumber(observation.worldZ)) {
    return "Coordinates must be finite numbers.";
  }

  const coordinateLimit = 1_000_000;
  if (Math.abs(observation.worldX) > coordinateLimit
    || Math.abs(observation.worldY) > coordinateLimit
    || Math.abs(observation.worldZ) > coordinateLimit) {
    return "Coordinates are outside the accepted range.";
  }

  if (isUnloadAltitude(observation.worldY!)) {
    return "Coordinates are at unload / inside-floor altitude.";
  }

  if (!isAcceptableString(observation.installationHash, true, 128)) {
    return "installationHash is required.";
  }

  if (!isAcceptableString(observation.pluginVersion, true, 64)) {
    return "pluginVersion is required.";
  }

  if (!isAcceptableString(observation.gameVersion, false, 64)
    || !isAcceptableString(observation.cofferType, false, 64)
    || !isAcceptableString(observation.observedAtUtc, true, 64)) {
    return "One or more string fields are invalid.";
  }

  if (!UTC_TIMESTAMP_PATTERN.test(observation.observedAtUtc!)) {
    return "observedAtUtc must be an ISO-8601 UTC timestamp.";
  }

  const observedAt = Date.parse(observation.observedAtUtc!);
  const now = Date.now();
  if (!Number.isFinite(observedAt)) {
    return "observedAtUtc is invalid.";
  }

  if (observedAt > now + 10 * 60 * 1000) {
    return "Observation is too far in the future.";
  }

  if (observedAt < now - 7 * 24 * 60 * 60 * 1000) {
    return "Observation is too old.";
  }

  return null;
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

function validateCarrotLocation(value: unknown): string | null {
  if (value === null || typeof value !== "object" || Array.isArray(value)) {
    return "Body must be a JSON object.";
  }

  const observation = value as Partial<CarrotLocationRequest>;
  if (!isIntegerInRange(observation.territoryId, 1, 100_000)) {
    return "Invalid territoryId.";
  }

  if (!OCCULT_CRESCENT_TERRITORY_IDS.has(observation.territoryId!)) {
    return "territoryId must be an Occult Crescent zone.";
  }

  if (observation.objectBaseId !== null
    && observation.objectBaseId !== undefined
    && observation.objectBaseId !== CARROT_OBJECT_BASE_ID) {
    return "objectBaseId must be the Occult Crescent carrot object.";
  }

  if (!isFiniteNumber(observation.worldX)
    || !isFiniteNumber(observation.worldY)
    || !isFiniteNumber(observation.worldZ)) {
    return "Coordinates must be finite numbers.";
  }

  const coordinateLimit = 1_000_000;
  if (Math.abs(observation.worldX) > coordinateLimit
    || Math.abs(observation.worldY) > coordinateLimit
    || Math.abs(observation.worldZ) > coordinateLimit) {
    return "Coordinates are outside the accepted range.";
  }

  if (isUnloadAltitude(observation.worldY!)) {
    return "Coordinates are at unload / inside-floor altitude.";
  }

  if (!isAcceptableString(observation.installationHash, true, 128)) {
    return "installationHash is required.";
  }

  if (!isAcceptableString(observation.pluginVersion, true, 64)) {
    return "pluginVersion is required.";
  }

  if (!isAcceptableString(observation.observedAtUtc, true, 64)) {
    return "observedAtUtc is required.";
  }

  if (!UTC_TIMESTAMP_PATTERN.test(observation.observedAtUtc!)) {
    return "observedAtUtc must be an ISO-8601 UTC timestamp.";
  }

  const observedAt = Date.parse(observation.observedAtUtc!);
  const now = Date.now();
  if (!Number.isFinite(observedAt)) {
    return "observedAtUtc is invalid.";
  }

  if (observedAt > now + 10 * 60 * 1000) {
    return "Observation is too far in the future.";
  }

  if (observedAt < now - 7 * 24 * 60 * 60 * 1000) {
    return "Observation is too old.";
  }

  return null;
}

function validatePotCycle(value: unknown): string | null {
  if (value === null || typeof value !== "object" || Array.isArray(value)) {
    return "Body must be a JSON object.";
  }

  const pot = value as Partial<PotCycleRequest>;
  if (!isAcceptableString(pot.instanceKey, true, 64) || !INSTANCE_KEY_PATTERN.test(pot.instanceKey!)) {
    return "instanceKey must be a 64-character hex SHA-256 digest.";
  }

  if (!isIntegerInRange(pot.territoryId, 1, 100_000)) {
    return "Invalid territoryId.";
  }

  if (!OCCULT_CRESCENT_TERRITORY_IDS.has(pot.territoryId!)) {
    return "territoryId must be an Occult Crescent zone.";
  }

  if (!isIntegerInRange(pot.datacenterId, 1, 100_000)) {
    return "Invalid datacenterId.";
  }

  if (!isIntegerInRange(pot.potFateId, 1, 100_000) || !OCCULT_POT_FATE_IDS.has(pot.potFateId!)) {
    return "potFateId must be a known Occult Crescent pot FATE.";
  }

  if (!isIntegerInRange(pot.spawnAtUnix, 1_000_000_000, 4_000_000_000)) {
    return "Invalid spawnAtUnix.";
  }

  const nowUnix = Math.floor(Date.now() / 1000);
  if (pot.spawnAtUnix! > nowUnix + 10 * 60) {
    return "spawnAtUnix is too far in the future.";
  }

  if (pot.spawnAtUnix! < nowUnix - POT_CYCLE_MAX_AGE_SECONDS) {
    return "spawnAtUnix is too old.";
  }

  if (!isAcceptableString(pot.installationHash, true, 128)) {
    return "installationHash is required.";
  }

  if (!isAcceptableString(pot.pluginVersion, true, 64)) {
    return "pluginVersion is required.";
  }

  if (!isAcceptableString(pot.observedAtUtc, true, 64)) {
    return "observedAtUtc is required.";
  }

  if (!UTC_TIMESTAMP_PATTERN.test(pot.observedAtUtc!)) {
    return "observedAtUtc must be an ISO-8601 UTC timestamp.";
  }

  const observedAt = Date.parse(pot.observedAtUtc!);
  const now = Date.now();
  if (!Number.isFinite(observedAt)) {
    return "observedAtUtc is invalid.";
  }

  if (observedAt > now + 10 * 60 * 1000) {
    return "Observation is too far in the future.";
  }

  if (observedAt < now - 7 * 24 * 60 * 60 * 1000) {
    return "Observation is too old.";
  }

  return null;
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
      object_base_id, installation_hash, plugin_version, observed_at_utc
    )
    SELECT ?, ?, ?, ?, ?, ?, ?, ?
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
      coffer_type, installation_hash, plugin_version,
      game_version, observed_at_utc
    )
    SELECT ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?
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

export default {
  async fetch(request: Request, env: Env, ctx: ExecutionContext): Promise<Response> {
    const url = new URL(request.url);

    if (request.method === "GET" && url.pathname === "/health") {
      return jsonResponse({ status: "ok" });
    }

    if (request.method === "GET" && url.pathname === "/api/v1/candidates") {
      try {
        return await listAcceptedCandidatesPublic(request, env, ctx);
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
        return await listAcceptedCarrotLocationsPublic(request, env, ctx);
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
          return await listCandidates(request, env);
        }

        if (request.method === "GET" && url.pathname === "/api/v1/admin/export/accepted-candidates") {
          return await exportAcceptedCandidates(request, env);
        }

        if (request.method === "GET" && url.pathname === "/api/v1/admin/carrot-candidates") {
          return await listCarrotCandidates(request, env);
        }

        if (request.method === "GET" && url.pathname === "/api/v1/admin/export/accepted-carrot-locations") {
          return await exportAcceptedCarrotLocations(request, env);
        }

        const candidateDetailMatch = url.pathname.match(/^\/api\/v1\/admin\/candidates\/(\d+)$/);
        if (candidateDetailMatch !== null && request.method === "GET") {
          const candidateId = parsePositiveInteger(candidateDetailMatch[1]);
          return candidateId === null
            ? jsonResponse({ error: "Invalid candidate ID." }, 400)
            : await getCandidateDetail(candidateId, env);
        }

        const candidateReviewMatch = url.pathname.match(/^\/api\/v1\/admin\/candidates\/(\d+)\/review$/);
        if (candidateReviewMatch !== null && request.method === "POST") {
          const candidateId = parsePositiveInteger(candidateReviewMatch[1]);
          return candidateId === null
            ? jsonResponse({ error: "Invalid candidate ID." }, 400)
            : await reviewCandidate(request, candidateId, env);
        }

        const carrotDetailMatch = url.pathname.match(/^\/api\/v1\/admin\/carrot-candidates\/(\d+)$/);
        if (carrotDetailMatch !== null && request.method === "GET") {
          const candidateId = parsePositiveInteger(carrotDetailMatch[1]);
          return candidateId === null
            ? jsonResponse({ error: "Invalid candidate ID." }, 400)
            : await getCarrotCandidateDetail(candidateId, env);
        }

        const carrotReviewMatch = url.pathname.match(/^\/api\/v1\/admin\/carrot-candidates\/(\d+)\/review$/);
        if (carrotReviewMatch !== null && request.method === "POST") {
          const candidateId = parsePositiveInteger(carrotReviewMatch[1]);
          return candidateId === null
            ? jsonResponse({ error: "Invalid candidate ID." }, 400)
            : await reviewCarrotCandidate(request, candidateId, env);
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

        const submitted = await submitObservation(request, env);
        if (submitted.observationId !== null) {
          ctx.waitUntil(processObservationById(env, submitted.observationId).then(async result => {
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

        const submitted = await submitCarrotLocation(request, env);
        if (submitted.observationId !== null) {
          ctx.waitUntil(processCarrotObservationById(env, submitted.observationId).then(async result => {
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
    const cofferResult = await processPendingObservations(env);
    console.log("Observation processor completed", cofferResult);
    const carrotResult = await processPendingCarrotObservations(env);
    console.log("Carrot processor completed", carrotResult);
    const rejectedUnload = await rejectUnloadAltitudeCandidates(env);
    console.log("Unload-altitude candidate reject completed", rejectedUnload);
    if (cofferResult.newlyAccepted > 0
      || carrotResult.newlyAccepted > 0
      || rejectedUnload.coffers > 0
      || rejectedUnload.carrots > 0) {
      await invalidateAcceptedCatalogCaches();
    }

    const prunedPots = await pruneStalePotCycles(env);
    console.log("Pot cycle prune completed", prunedPots);
    const prunedObservations = await pruneProcessedObservations(env);
    console.log("Processed observation prune completed", prunedObservations);
  },
} satisfies ExportedHandler<Env>;

/** Drop accepted pads whose centroid is unload / inside-floor junk (hamlet basement is ~−162). */
async function rejectUnloadAltitudeCandidates(
  env: Env,
): Promise<{ coffers: number; carrots: number }> {
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

  return {
    coffers: coffers.meta.changes ?? 0,
    carrots: carrots.meta.changes ?? 0,
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

/** Drop clustered coffer/carrot rows (and their member links) older than the retain window. */
async function pruneProcessedObservations(
  env: Env,
): Promise<{ coffers: number; carrots: number }> {
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
  return { coffers, carrots };
}

async function pruneProcessedTable(
  env: Env,
  membersTable: "observation_candidate_members" | "carrot_candidate_members",
  observationsTable: "observations" | "carrot_observations",
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
