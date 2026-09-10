/** Hamlet basement ~−162. Unload ghosts −500 to −980. Match TreasurePathing.UnloadAltitudeMax. */
export const MIN_VALID_WORLD_Y = -250;

export function isUnloadAltitude(y: number): boolean {
  return y < MIN_VALID_WORLD_Y;
}
