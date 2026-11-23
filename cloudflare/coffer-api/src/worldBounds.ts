/** Hamlet basement ~−162. Unload ghosts −500 to −980. Match TreasurePathing.UnloadAltitudeMax. */
export const MIN_VALID_WORLD_Y = -250;

/** Both Occult Crescent maps span ±1024 yalms; shipped spots reach ±968 and y 260 at most. */
const MAX_WORLD_XZ = 1024;
const MAX_WORLD_Y = 500;

export function isUnloadAltitude(y: number): boolean {
  return y < MIN_VALID_WORLD_Y;
}

export function isInsideZoneBox(x: number, y: number, z: number): boolean {
  return Math.abs(x) <= MAX_WORLD_XZ
    && Math.abs(z) <= MAX_WORLD_XZ
    && y >= MIN_VALID_WORLD_Y
    && y <= MAX_WORLD_Y;
}
