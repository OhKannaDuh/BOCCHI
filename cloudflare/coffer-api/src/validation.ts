import { isInsideZoneBox, isUnloadAltitude } from "./worldBounds.ts";

export interface CofferObservationRequest {
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

export interface CarrotLocationRequest {
  territoryId: number;
  worldX: number;
  worldY: number;
  worldZ: number;
  objectBaseId?: number | null;
  installationHash: string;
  pluginVersion: string;
  observedAtUtc: string;
}

export interface PotChestLocationRequest {
  territoryId: number;
  potFateId: number;
  isReroll: boolean;
  worldX: number;
  worldY: number;
  worldZ: number;
  installationHash: string;
  pluginVersion: string;
  observedAtUtc: string;
}

export interface PotCycleRequest {
  instanceKey: string;
  territoryId: number;
  datacenterId: number;
  potFateId: number;
  spawnAtUnix: number;
  installationHash: string;
  pluginVersion: string;
  observedAtUtc: string;
}

const MAX_STRING_LENGTH = 128;
const UTC_TIMESTAMP_PATTERN = /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?(?:Z|\+00:00)$/;
export const INSTANCE_KEY_PATTERN = /^[0-9A-Fa-f]{64}$/;
/** South Horn (1252) and North Horn (1346). */
export const OCCULT_CRESCENT_TERRITORY_IDS = new Set([1252, 1346]);
/** Persistent / Pleading pots (SH) and Daylight / Pot of Bother (NH). */
const OCCULT_POT_FATE_IDS = new Set([1976, 1977, 2072, 2073]);
/** OccultObjectType.Carrot */
export const CARROT_OBJECT_BASE_ID = 2010139;
/** Only return pot anchors newer than this (seconds). */
export const POT_CYCLE_MAX_AGE_SECONDS = 45 * 60;
/** Clients send the game's FATE start time (server clock), so only clock jitter can be ahead. */
const POT_CYCLE_MAX_FUTURE_SECONDS = 60;

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

type Report = {
  territoryId?: unknown;
  worldX?: unknown;
  worldY?: unknown;
  worldZ?: unknown;
  installationHash?: unknown;
  pluginVersion?: unknown;
  observedAtUtc?: unknown;
};

function validateReport(value: unknown, withPosition: boolean): string | null {
  if (value === null || typeof value !== "object" || Array.isArray(value)) {
    return "Body must be a JSON object.";
  }

  const report = value as Report;
  if (!isIntegerInRange(report.territoryId, 1, 100_000)) {
    return "Invalid territoryId.";
  }

  if (!OCCULT_CRESCENT_TERRITORY_IDS.has(report.territoryId)) {
    return "territoryId must be an Occult Crescent zone.";
  }

  if (withPosition) {
    if (!isFiniteNumber(report.worldX) || !isFiniteNumber(report.worldY) || !isFiniteNumber(report.worldZ)) {
      return "Coordinates must be finite numbers.";
    }

    if (isUnloadAltitude(report.worldY)) {
      return "Coordinates are at unload / inside-floor altitude.";
    }

    if (!isInsideZoneBox(report.worldX, report.worldY, report.worldZ)) {
      return "Coordinates are outside the accepted range.";
    }
  }

  if (!isAcceptableString(report.installationHash, true, 128)) {
    return "installationHash is required.";
  }

  if (!isAcceptableString(report.pluginVersion, true, 64)) {
    return "pluginVersion is required.";
  }

  if (!isAcceptableString(report.observedAtUtc, true, 64)) {
    return "observedAtUtc is required.";
  }

  if (!UTC_TIMESTAMP_PATTERN.test(report.observedAtUtc as string)) {
    return "observedAtUtc must be an ISO-8601 UTC timestamp.";
  }

  const observedAt = Date.parse(report.observedAtUtc as string);
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

export function validateObservation(value: unknown): string | null {
  const error = validateReport(value, true);
  if (error !== null) {
    return error;
  }

  const observation = value as Partial<CofferObservationRequest>;
  // BOCCHI accepts any positive coffer BaseId in OC (silver/bronze hunt + pot-reveal).
  if (!isIntegerInRange(observation.dataId, 1, 4_294_967_295)) {
    return "Invalid dataId.";
  }

  if (observation.mapId !== null
    && observation.mapId !== undefined
    && !isIntegerInRange(observation.mapId, 1, 100_000)) {
    return "Invalid mapId.";
  }

  if (!isAcceptableString(observation.gameVersion, false, 64)
    || !isAcceptableString(observation.cofferType, false, 64)) {
    return "One or more string fields are invalid.";
  }

  return null;
}

export function validateCarrotLocation(value: unknown): string | null {
  const error = validateReport(value, true);
  if (error !== null) {
    return error;
  }

  const observation = value as Partial<CarrotLocationRequest>;
  if (observation.objectBaseId !== null
    && observation.objectBaseId !== undefined
    && observation.objectBaseId !== CARROT_OBJECT_BASE_ID) {
    return "objectBaseId must be the Occult Crescent carrot object.";
  }

  return null;
}

export function validatePotChestLocation(value: unknown): string | null {
  const error = validateReport(value, true);
  if (error !== null) {
    return error;
  }

  const observation = value as Partial<PotChestLocationRequest>;
  if (!isIntegerInRange(observation.potFateId, 1, 100_000) || !OCCULT_POT_FATE_IDS.has(observation.potFateId)) {
    return "potFateId must be a known Occult Crescent pot FATE.";
  }

  if (typeof observation.isReroll !== "boolean") {
    return "isReroll must be a boolean.";
  }

  return null;
}

export function validatePotCycle(value: unknown): string | null {
  const error = validateReport(value, false);
  if (error !== null) {
    return error;
  }

  const pot = value as Partial<PotCycleRequest>;
  if (!isAcceptableString(pot.instanceKey, true, 64) || !INSTANCE_KEY_PATTERN.test(pot.instanceKey!)) {
    return "instanceKey must be a 64-character hex SHA-256 digest.";
  }

  if (!isIntegerInRange(pot.datacenterId, 1, 100_000)) {
    return "Invalid datacenterId.";
  }

  if (!isIntegerInRange(pot.potFateId, 1, 100_000) || !OCCULT_POT_FATE_IDS.has(pot.potFateId)) {
    return "potFateId must be a known Occult Crescent pot FATE.";
  }

  if (!isIntegerInRange(pot.spawnAtUnix, 1_000_000_000, 4_000_000_000)) {
    return "Invalid spawnAtUnix.";
  }

  const nowUnix = Math.floor(Date.now() / 1000);
  if (pot.spawnAtUnix > nowUnix + POT_CYCLE_MAX_FUTURE_SECONDS) {
    return "spawnAtUnix is too far in the future.";
  }

  if (pot.spawnAtUnix < nowUnix - POT_CYCLE_MAX_AGE_SECONDS) {
    return "spawnAtUnix is too old.";
  }

  return null;
}
