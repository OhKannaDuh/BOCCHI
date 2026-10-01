using System.Numerics;

namespace BOCCHI.Treasure.Hunt;

public static class HuntDistances
{
    public const float MatchRadius = 120f;

    public const float MatchRadiusSq = MatchRadius * MatchRadius;

    public const float LayoutProximityRadius = 25f;

    public const float LayoutProximityRadiusSq = LayoutProximityRadius * LayoutProximityRadius;

    public const float SamePadRecheckRadiusSq = 20f * 20f;

    public const float EmptyPadSkipRadius = 100f;

    public const float EmptyPadEarlySkipRadius = 60f;

    public const float SameFloorVerticalTolerance = 12f;

    public static bool IsSameFloor(Vector3 a, Vector3 b) =>
        MathF.Abs(a.Y - b.Y) <= SameFloorVerticalTolerance;

    public const float ShelfSeparationYalms = 40f;

    public static bool IsSeparatedShelf(Vector3 a, Vector3 b) =>
        MathF.Abs(a.Y - b.Y) > ShelfSeparationYalms;

    public const float EmptyPadRegionTrustRadiusSq = LayoutProximityRadius * LayoutProximityRadius;

    public static readonly TimeSpan EmptyPadConfirmDelay = TimeSpan.FromMilliseconds(600);

    public const float UseRadius = 2.0f;

    public const float BunnyInteractRadius = UseRadius;

    public const float DismountRadius = 15f;

    public const float StuckNearRadius = 3.5f;

    public static readonly TimeSpan StuckNearTimeout = TimeSpan.FromSeconds(6);

    public const float NearbyLiveDivertRange = MatchRadius;

    public const float NearbyLiveDivertMinCurrentDistance = 25f;

    public const float NearbyLiveDivertClearAdvantage = 5f;

    public const float LocalClusterRadius = 90f;
}
