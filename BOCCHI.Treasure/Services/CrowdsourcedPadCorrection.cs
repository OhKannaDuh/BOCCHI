using System.Numerics;

namespace BOCCHI.Treasure.Services;

public static class CrowdsourcedPadCorrection
{
    public const float CarrotMaxCorrection = 50f;

    public const float CarrotMaxCorrectionSq = CarrotMaxCorrection * CarrotMaxCorrection;

    public static Vector3 CorrectCofferPosition(
        uint dataId,
        Vector3 current,
        IReadOnlyList<CrowdsourcedCofferCandidate> crowd)
    {
        Vector3? best = null;
        float bestDistSq = float.MaxValue;
        foreach (CrowdsourcedCofferCandidate candidate in crowd)
        {
            if (candidate.DataId != dataId || TreasurePathing.IsUnloadAltitude(candidate.Position))
            {
                continue;
            }

            float distSq = Vector3.DistanceSquared(candidate.Position, current);
            if (distSq >= bestDistSq)
            {
                continue;
            }

            bestDistSq = distSq;
            best = candidate.Position;
        }

        if (best is { } corrected && bestDistSq > CofferLocationSyncService.MatchRadiusSq)
        {
            return corrected;
        }

        return current;
    }

    public static bool IsCofferCorrection(Vector3 before, Vector3 after) =>
        Vector3.DistanceSquared(before, after) > CofferLocationSyncService.MatchRadiusSq;
}
