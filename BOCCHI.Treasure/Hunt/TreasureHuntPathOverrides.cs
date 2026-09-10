using BOCCHI.Common.Data.Zones;
using System.Numerics;

namespace BOCCHI.Treasure.Hunt;

/// <summary>
///     Manual via-points for hunt walks where plain vnav hits hazards (wind updrafts, bad mesh).
///     Map coords use Dalamud MapUtil (SizeFactor 100, Offset ±1024) for North Horn.
/// </summary>
public static class TreasureHuntPathOverrides
{
    /// <summary>
    ///     Pads vnav cannot path to at all. Only list a pad here if vnav genuinely cannot route to it —
    ///     a pad that is merely awkward is better left in, since the stuck watch skips it for that run anyway.
    ///     Also remove the pad from treasure_route.json when adding it here.
    /// </summary>
    private static readonly HashSet<(ZoneId Zone, uint NodeId)> UnreachableNodes =
    [
        // Unhallowed Hamlet basement — vnav cannot finish the stair run reliably; high death risk.
        (ZoneId.NorthHorn, 2072u),
    ];

    /// <summary>
    ///     Far-northeast sinking ridge — dense packs break Hide / kill on approach.
    ///     Map ~33–38x, ~2–4y (2010 / 2054 / 2055 / 2057).
    /// </summary>
    private static readonly HashSet<(ZoneId Zone, uint NodeId)> DensePackApproachNodes =
    [
        (ZoneId.NorthHorn, 2010u),
        (ZoneId.NorthHorn, 2054u),
        (ZoneId.NorthHorn, 2055u),
        (ZoneId.NorthHorn, 2057u),
    ];

    /// <summary>
    ///     Minimum Hide enter distance on <see cref="DensePackApproachNodes"/> (yalms).
    ///     User config still wins when set higher.
    /// </summary>
    public const float DensePackHideEnterYalms = 25f;

    /// <summary>True when this pad is knowingly unreachable and must be left out of the route.</summary>
    public static bool IsUnreachable(ZoneId zone, uint nodeId) => UnreachableNodes.Contains((zone, nodeId));

    /// <summary>True when approaching this pad should avoid stuck lateral nudges and Hide earlier.</summary>
    public static bool IsDensePackApproach(ZoneId zone, uint nodeId) =>
        DensePackApproachNodes.Contains((zone, nodeId));

    /// <summary>Reach before opening the coffer.</summary>
    private static readonly Dictionary<(ZoneId Zone, uint NodeId), Vector3[]> ApproachByNode = new()
    {
        // Suspended Masonry_9 — map ~5.4, 34.1; plain path cuts through wind.
        // Approach via map 3.4, 34.2 then the chest.
        [(ZoneId.NorthHorn, 2061)] =
        [
            new(-904f, 157.8f, 636f),
        ],
        // Suspended Masonry lower pad — map ~8.6, 35.8; vnav cuts off the island edge (#173).
        // Keep the near island via only — (-700,160,800) is off-mesh (~100y west) and pathfind fails.
        // Do not use this via when leaving for 2037 (Y~86): it stays on the island. Cliff
        // hops drop the departure via and Return when the walk idles at the edge.
        [(ZoneId.NorthHorn, 2058)] =
        [
            new(-640f, 160.1f, 780f),
        ],
    };

    /// <summary>Leave through after the coffer so the next leg does not re-enter the hazard.</summary>
    private static readonly Dictionary<(ZoneId Zone, uint NodeId), Vector3[]> DepartureByNode = new()
    {
        // Map 3.1, 34.3 safe exit.
        [(ZoneId.NorthHorn, 2061)] =
        [
            new(-919f, 157.8f, 641f),
        ],
        [(ZoneId.NorthHorn, 2058)] =
        [
            new(-640f, 160.1f, 780f),
        ],
    };

    public static bool TryGetApproach(ZoneId zone, uint nodeId, out IReadOnlyList<Vector3> vias)
    {
        if (ApproachByNode.TryGetValue((zone, nodeId), out Vector3[]? points))
        {
            vias = points;
            return true;
        }

        vias = Array.Empty<Vector3>();
        return false;
    }

    public static bool TryGetDeparture(ZoneId zone, uint nodeId, out IReadOnlyList<Vector3> vias)
    {
        if (DepartureByNode.TryGetValue((zone, nodeId), out Vector3[]? points))
        {
            vias = points;
            return true;
        }

        vias = Array.Empty<Vector3>();
        return false;
    }
}
