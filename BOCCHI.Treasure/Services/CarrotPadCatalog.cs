using BOCCHI.Common.Data.Zones.Graph;
using System.Numerics;

namespace BOCCHI.Treasure.Services;

/// <summary>
///     Merge baked Carrot Hunt pads with worker-accepted locations.
///     Keeps baked ids (path overrides); remote-only pads use <c>1000 + candidateId</c>.
/// </summary>
public static class CarrotPadCatalog
{
    /// <summary>Match remote centroids to baked pads (worker clusters at ~1.5y).</summary>
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

        List<CarrotData> merged = [.. baked];
        HashSet<int> usedIds = baked.Select(b => b.Id).ToHashSet();

        foreach (AcceptedCarrotLocation location in remote)
        {
            if (TreasurePathing.IsUnloadAltitude(location.Position)
                || baked.Any(b => Vector3.DistanceSquared(b.Position, location.Position) <= MergeRadiusSq))
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
}

public readonly record struct AcceptedCarrotLocation(int CandidateId, ushort TerritoryId, Vector3 Position);
