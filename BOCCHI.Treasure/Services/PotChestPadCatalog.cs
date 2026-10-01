using BOCCHI.Common.Data.Zones.Graph;
using System.Numerics;

namespace BOCCHI.Treasure.Services;

public static class PotChestPadCatalog
{
    public const float MergeRadius = 3f;

    public const float MergeRadiusSq = MergeRadius * MergeRadius;

    public const float MaxCorrectionSq = CrowdsourcedPadCorrection.CarrotMaxCorrectionSq;

    public static List<PotChestData> Merge(
        IReadOnlyList<PotChestData> baked,
        IReadOnlyList<AcceptedPotChestLocation> remote)
    {
        if (remote.Count == 0)
        {
            return baked.ToList();
        }

        if (baked.Count == 0)
        {
            return remote
                .Where(r => !TreasurePathing.IsUnloadAltitude(r.Position))
                .Select(r => new PotChestData(r.Position, 99))
                .ToList();
        }

        List<PotChestData> merged = CorrectBaked(baked, remote);
        foreach (AcceptedPotChestLocation location in remote)
        {
            if (TreasurePathing.IsUnloadAltitude(location.Position)
                || merged.Any(b => Vector3.DistanceSquared(b.Position, location.Position) <= MergeRadiusSq))
            {
                continue;
            }

            merged.Add(new PotChestData(location.Position, 99));
        }

        return merged;
    }

    private static List<PotChestData> CorrectBaked(
        IReadOnlyList<PotChestData> baked,
        IReadOnlyList<AcceptedPotChestLocation> remote)
    {
        List<PotChestData> result = new(baked.Count);
        for (int bi = 0; bi < baked.Count; bi++)
        {
            PotChestData bake = baked[bi];
            int bestRemote = -1;
            float bestSq = float.MaxValue;
            for (int ri = 0; ri < remote.Count; ri++)
            {
                Vector3 remotePos = remote[ri].Position;
                if (TreasurePathing.IsUnloadAltitude(remotePos))
                {
                    continue;
                }

                float distSq = Vector3.DistanceSquared(bake.Position, remotePos);
                if (distSq >= bestSq)
                {
                    continue;
                }

                bestSq = distSq;
                bestRemote = ri;
            }

            if (bestRemote < 0 || bestSq <= MergeRadiusSq || bestSq > MaxCorrectionSq)
            {
                result.Add(bake);
                continue;
            }

            Vector3 chosen = remote[bestRemote].Position;
            float nearestBakeSq = float.MaxValue;
            int nearestBake = -1;
            for (int bj = 0; bj < baked.Count; bj++)
            {
                float distSq = Vector3.DistanceSquared(baked[bj].Position, chosen);
                if (distSq >= nearestBakeSq)
                {
                    continue;
                }

                nearestBakeSq = distSq;
                nearestBake = bj;
            }

            if (nearestBake != bi)
            {
                result.Add(bake);
                continue;
            }

            result.Add(bake with { Position = chosen });
        }

        return result;
    }
}

public readonly record struct AcceptedPotChestLocation(
    int CandidateId,
    ushort TerritoryId,
    int PotFateId,
    bool IsReroll,
    Vector3 Position);
