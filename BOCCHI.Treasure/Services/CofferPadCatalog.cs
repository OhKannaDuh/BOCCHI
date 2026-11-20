using BOCCHI.Common.Data.Zones.Graph;
using BOCCHI.Treasure.Hunt;
using System.Numerics;

namespace BOCCHI.Treasure.Services;

public static class CofferPadCatalog
{
    public static int MergeBaked(List<TreasureLayoutDatum> pads, List<TreasureData> baked)
    {
        HashSet<uint> present = pads.Select(t => t.Id).ToHashSet();
        int added = 0;
        foreach (TreasureData pad in baked)
        {
            if (pad.Position is not { } position || !present.Add((uint)pad.Id))
            {
                continue;
            }

            pads.Add(new((uint)pad.Id, position, TreasureCoffer.BronzeSgbId));
            added++;
        }

        return added;
    }

    public static int ApplyCorrections(List<TreasureLayoutDatum> pads, IReadOnlyList<CrowdsourcedCofferCandidate> crowd)
    {
        if (crowd.Count == 0)
        {
            return 0;
        }

        int corrected = 0;
        for (int i = 0; i < pads.Count; i++)
        {
            TreasureLayoutDatum pad = pads[i];
            Vector3 next = CrowdsourcedPadCorrection.CorrectCofferPosition(pad.Id, pad.Position, crowd);
            if (!CrowdsourcedPadCorrection.IsCofferCorrection(pad.Position, next))
            {
                continue;
            }

            pads[i] = pad with { Position = next };
            corrected++;
        }

        return corrected;
    }

    public static int MergeCrowdsourced(List<TreasureLayoutDatum> pads, IReadOnlyList<CrowdsourcedCofferCandidate> crowd)
    {
        int added = 0;
        foreach (CrowdsourcedCofferCandidate spot in crowd)
        {
            if (TreasurePathing.IsUnloadAltitude(spot.Position))
            {
                continue;
            }

            if (pads.Any(t => Vector3.DistanceSquared(t.Position, spot.Position) <= CofferLocationSyncService.MatchRadiusSq))
            {
                continue;
            }

            if (pads.Any(t => t.Id == spot.DataId))
            {
                continue;
            }

            pads.Add(new(spot.DataId, spot.Position, TreasureCoffer.BronzeSgbId));
            added++;
        }

        return added;
    }
}
