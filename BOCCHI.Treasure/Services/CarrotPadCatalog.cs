using BOCCHI.Common.Data.Zones.Graph;
using System.Numerics;

namespace BOCCHI.Treasure.Services;

public static class CarrotPadCatalog
{
    public const float MergeRadius = 3f;

    public const float MergeRadiusSq = MergeRadius * MergeRadius;

    public const int RemoteIdOffset = 1000;

    public static List<CarrotData> Merge(
        IReadOnlyList<CarrotData> baked,
        IReadOnlyList<AcceptedCarrotLocation> remote)
    {
        if (baked.Count == 0)
        {
            return [];
        }

        if (remote.Count == 0)
        {
            return baked.ToList();
        }

        List<CarrotData> merged = CorrectBaked(baked, remote);
        HashSet<int> usedIds = merged.Select(b => b.Id).ToHashSet();

        foreach (AcceptedCarrotLocation location in remote)
        {
            if (TreasurePathing.IsUnloadAltitude(location.Position)
                || merged.Any(b => Vector3.DistanceSquared(b.Position, location.Position) <= MergeRadiusSq))
            {
                continue;
            }

            int id = RemoteIdOffset + location.CandidateId;
            if (!usedIds.Add(id))
            {
                continue;
            }

            merged.Add(new CarrotData(id, location.Position, 0));
        }

        return merged;
    }

    private static List<CarrotData> CorrectBaked(
        IReadOnlyList<CarrotData> baked,
        IReadOnlyList<AcceptedCarrotLocation> remote)
    {
        List<CarrotData> result = new(baked.Count);
        for (int bi = 0; bi < baked.Count; bi++)
        {
            CarrotData bake = baked[bi];
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

            if (bestRemote < 0
                || bestSq <= MergeRadiusSq
                || bestSq > CrowdsourcedPadCorrection.CarrotMaxCorrectionSq)
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

public readonly record struct AcceptedCarrotLocation(int CandidateId, ushort TerritoryId, Vector3 Position);
