using Ocelot.Services.Logger;
using Ocelot.Services.Pathfinding;
using System.Numerics;
using Path = Ocelot.Services.Pathfinding.Path;

namespace BOCCHI.Common.Data.Zones.Graph;

public enum ActivityAreaShape
{
    Circle = 0,

    Square = 1,
}

public record ActivityData(
    int Id,
    Vector3 Position,
    uint? PreferredAethernetId = null,
    ActivityAreaShape AreaShape = ActivityAreaShape.Circle,
    float? StandRadius = null,
    float? CombatRadius = null);

public record CarrotData(int Id, Vector3 Position, int Level);

public record TreasureData(int Id, int Level, Vector3? Position = null)
{
    private const float PositionMatchDistanceSquared = 4f;

    public bool Matches(uint treasureRowId, Vector3 worldPosition) =>
        Id == treasureRowId
        || Position is { } position && Vector3.DistanceSquared(position, worldPosition) <= PositionMatchDistanceSquared;

    public static bool TryResolveLevel(
        uint layoutId,
        Vector3 layoutPosition,
        IReadOnlyList<TreasureData> treasureData,
        out int level)
    {
        TreasureData? byId = treasureData.FirstOrDefault(entry => entry.Id == layoutId);
        if (byId != null)
        {
            level = byId.Level;
            return true;
        }

        TreasureData? nearest = null;
        float nearestSq = float.MaxValue;
        foreach (TreasureData entry in treasureData)
        {
            if (entry.Position is not { } position)
            {
                continue;
            }

            float distSq = Vector3.DistanceSquared(position, layoutPosition);
            if (distSq > PositionMatchDistanceSquared || distSq >= nearestSq)
            {
                continue;
            }

            nearestSq = distSq;
            nearest = entry;
        }

        if (nearest != null)
        {
            level = nearest.Level;
            return true;
        }

        level = 0;
        return false;
    }
}

public record PotChestData(Vector3 Position, int Level);

public class GraphConfig(IPathfinder pathfinder, ILogger logger)
{
#if DEBUG
    public static readonly List<List<Vector3>> DebugPathLines = [];
#endif

    public float TeleportCost { get; init; } = 10f;

    public async Task<float> GetWalkingCost(Vector3 from, Vector3 to)
    {
        logger.Debug($"Calculating walking cost (from = {from:f2}, to = {to:f2})");
        Path result = await pathfinder.Pathfind(new(to)
        {
            From = from,
            AllowFlying = false
        });

#if DEBUG
        DebugPathLines.Add(result.Nodes.ToList());
#endif

        return result.CostOrUnreachable();
    }

    public async Task<float> GetWalkingCost(Node from, Node to) => await GetWalkingCost(from.Position, to.Position);
}

public static class PathReachability
{
    public static bool IsReachable(this Path path) => path.Nodes.Count >= 2;

    public static float CostOrUnreachable(this Path path) =>
        path.IsReachable() ? path.Distance : float.PositiveInfinity;
}
