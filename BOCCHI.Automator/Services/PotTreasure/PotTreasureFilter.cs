using System.Numerics;
using BOCCHI.Common.Data.Zones;
using BOCCHI.Common.Data.Zones.Graph;
using Ocelot.Extensions;

namespace BOCCHI.Automator.Services.PotTreasure;

public static class PotTreasureFilter
{
    public const float RevealSpotTolerance = 22f;

    public static bool IsOnAuthoredPotSpot(
        Vector3 position,
        IEnumerable<Vector3> potSpots,
        IEnumerable<Vector3> foreignSpots,
        float tolerance = RevealSpotTolerance)
    {
        float nearestPot = float.MaxValue;
        foreach (Vector3 spot in potSpots)
        {
            nearestPot = MathF.Min(nearestPot, position.Distance2D(spot));
        }

        if (nearestPot > tolerance)
        {
            return false;
        }

        foreach (Vector3 known in foreignSpots)
        {
            if (position.Distance2D(known) < nearestPot)
            {
                return false;
            }
        }

        return true;
    }

    public const float OctantTolerance = 22.5f;

    public const float WideTolerance = 67.5f;

    public static float Bearing(Vector3 from, Vector3 to)
    {
        float deg = MathF.Atan2(to.X - from.X, -(to.Z - from.Z)) * (180f / MathF.PI);
        return deg < 0f ? deg + 360f : deg;
    }

    public static float AngleDelta(float a, float b)
    {
        float delta = MathF.Abs(a - b) % 360f;
        return delta > 180f ? 360f - delta : delta;
    }

    public static float? HintBearing(PotTreasureDirection direction) => direction switch
    {
        PotTreasureDirection.North => 0f,
        PotTreasureDirection.Northeast => 45f,
        PotTreasureDirection.East => 90f,
        PotTreasureDirection.Southeast => 135f,
        PotTreasureDirection.South => 180f,
        PotTreasureDirection.Southwest => 225f,
        PotTreasureDirection.West => 270f,
        PotTreasureDirection.Northwest => 315f,
        _ => null,
    };

    public static List<PotTreasureCandidate> Narrow(
        IEnumerable<PotTreasureCandidate> pool,
        Vector3 from,
        PotTreasureDirection direction,
        PotTreasureDistanceBucket distance,
        float toleranceDegrees)
    {
        if (HintBearing(direction) is not float hinted)
        {
            return pool.ToList();
        }

        float expected = PotTreasureIds.RefineStep(distance);
        return pool
            .Where(c => c.Position.Distance2D(from) > 1f)
            .Where(c => AngleDelta(Bearing(from, c.Position), hinted) <= toleranceDegrees)
            .OrderBy(c => MathF.Abs(c.Position.Distance2D(from) - expected))
            .ThenBy(c => c.Position.Distance2D(from))
            .ToList();
    }

    public static List<PotTreasureCandidate> BuildRerollPool(IReadOnlyList<PotChestData> rerolls) =>
        rerolls
            .Select((chest, i) => new PotTreasureCandidate($"R{i + 1}", chest.Position, chest.Level))
            .ToList();

    public static bool CanRunSmart(IReadOnlyList<PotChestData> primaryPads) =>
        primaryPads.Count > 0;

    public static List<PotTreasureCandidate> BuildPool(IReadOnlyList<PotChestData> chests) =>
        chests
            .Select((chest, i) => new PotTreasureCandidate($"P{i + 1}", chest.Position, chest.Level))
            .ToList();
}
