using System.Numerics;

namespace BOCCHI.Treasure.Hunt;

/// <summary>
/// Shared treasure / carrot hunt distances (empty-skip, divert, pad match, carrot use).
/// </summary>
public static class HuntDistances
{
    /// <summary>Match live objects to a layout/authored pad within this distance.</summary>
    public const float MatchRadius = 120f;

    public const float MatchRadiusSq = MatchRadius * MatchRadius;

    /// <summary>Tight layout↔live / authored level match (pad vs object).</summary>
    public const float LayoutProximityRadius = 25f;

    public const float LayoutProximityRadiusSq = LayoutProximityRadius * LayoutProximityRadius;

    /// <summary>Same-pad double-spawn recheck after opening a bunny.</summary>
    public const float SamePadRecheckRadiusSq = 20f * 20f;

    /// <summary>
    ///     Carrot Hunt: trust an empty pad this close without a nearby live carrot.
    ///     Treasure Hunt empty-skip distance comes from <c>TreasureConfig.EmptyPadTrustDistance</c>;
    ///     divert peel still uses <see cref="EmptyPadSkipRadius"/> so it does not U-turn off a pad
    ///     that is about to empty-skip.
    /// </summary>
    public const float EmptyPadSkipRadius = 100f;

    /// <summary>
    ///     Treasure hunt may skip an empty pad this far out only when a nearby coffer
    ///     proves the region has streamed.
    /// </summary>
    public const float EmptyPadEarlySkipRadius = 60f;

    /// <summary>
    /// Empty-skip / open arrival require |ΔY| within this — otherwise a surface stand
    /// above a basement coffer looks “on the pad” in 2D and the hunt turns around.
    /// </summary>
    public const float SameFloorVerticalTolerance = 12f;

    public static bool IsSameFloor(Vector3 a, Vector3 b) =>
        MathF.Abs(a.Y - b.Y) <= SameFloorVerticalTolerance;

    /// <summary>
    ///     Shelves this far apart are a cliff, not a ramp (Suspended Masonry island vs 2037).
    ///     <see cref="SameFloorVerticalTolerance"/> is for empty-skip / interact; this is for
    ///     “do not idle at the edge — Return / take a shard down”.
    /// </summary>
    public const float ShelfSeparationYalms = 40f;

    public static bool IsSeparatedShelf(Vector3 a, Vector3 b) =>
        MathF.Abs(a.Y - b.Y) > ShelfSeparationYalms;

    /// <summary>
    ///     Carrot Hunt: trust empty farther out when another live carrot proves the region
    ///     has streamed. Treasure Hunt uses <c>EmptyPadTrustDistance</c> instead.
    /// </summary>
    public const float EmptyPadRegionTrustRadiusSq = LayoutProximityRadius * LayoutProximityRadius;

    /// <summary>Require a short empty confirmation so object-table flicker does not false-skip.</summary>
    public static readonly TimeSpan EmptyPadConfirmDelay = TimeSpan.FromMilliseconds(600);

    /// <summary>Close enough (2D) to use a Fortune Carrot. Keep near chest interact range.</summary>
    public const float UseRadius = 2.0f;

    /// <summary>3D interact range for bunny chests (same as coffer open).</summary>
    public const float BunnyInteractRadius = UseRadius;

    /// <summary>Dismount once this close before using a Fortune Carrot.</summary>
    public const float DismountRadius = 15f;

    /// <summary>If approach stalls within this range (2D), try using / opening from here.</summary>
    public const float StuckNearRadius = 3.5f;

    public static readonly TimeSpan StuckNearTimeout = TimeSpan.FromSeconds(6);

    /// <summary>
    /// Mid-route divert / tour prefer: live unused target within this of the player.
    /// Matches pad match radius so ~100y radar hits still divert.
    /// </summary>
    public const float NearbyLiveDivertRange = MatchRadius;

    /// <summary>Don't divert when this close to the current walk goal.</summary>
    public const float NearbyLiveDivertMinCurrentDistance = 25f;

    /// <summary>Live target must be at least this many yalms closer than the current destination.</summary>
    public const float NearbyLiveDivertClearAdvantage = 5f;

    /// <summary>
    /// After finishing a pad, clear other unfinished pads within this range before hopping away
    /// (caves, citadel clusters). Also biases nearest-neighbor tour edges.
    /// </summary>
    public const float LocalClusterRadius = 90f;
}
