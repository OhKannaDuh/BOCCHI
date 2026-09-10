using System.Numerics;
using BOCCHI.Common.Data.Zones;
using BOCCHI.Common.Data.Zones.Graph;
using Ocelot.Extensions;

namespace BOCCHI.Automator.Services.PotTreasure;

/// <summary>
///     Compass hints are bearings from where Magical Elixir was used, not from the current feet.
/// </summary>
public static class PotTreasureFilter
{
    /// <summary>How close a revealed coffer must be to count as on a pot pad.</summary>
    public const float RevealSpotTolerance = 22f;

    /// <summary>True when a revealed treasure sits on a pot pad, not a nearer hunt coffer.</summary>
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

    /// <summary>Octants are 45° wide, so a hint constrains the bearing to ±22.5°.</summary>
    public const float OctantTolerance = 22.5f;

    /// <summary>Used for the one retry before giving up: 1.5 octants either side.</summary>
    public const float WideTolerance = 67.5f;

    /// <summary>Compass bearing in degrees from <paramref name="from"/>, 0 = north (−Z), 90 = east.</summary>
    public static float Bearing(Vector3 from, Vector3 to)
    {
        float deg = MathF.Atan2(to.X - from.X, -(to.Z - from.Z)) * (180f / MathF.PI);
        return deg < 0f ? deg + 360f : deg;
    }

    /// <summary>Smallest absolute angle between two bearings, 0..180.</summary>
    public static float AngleDelta(float a, float b)
    {
        float delta = MathF.Abs(a - b) % 360f;
        return delta > 180f ? 360f - delta : delta;
    }

    /// <summary>Bearing of the hinted octant, or null when the direction is unknown.</summary>
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

    /// <summary>
    ///     Spots lying in the hinted direction from <paramref name="from"/>, nearest first within the
    ///     distance band the hint reported. Distance only orders the result — the buckets' real
    ///     boundaries are not known, so excluding on them would risk discarding the right spot.
    /// </summary>
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

    /// <summary>
    ///     The reroll pads, as filter input. A second-chance chest hides among these rather than the
    ///     FATE's own spots — they sit in remote areas (250y from the nearest pot spot on average in
    ///     North Horn, up to 515y), so narrowing must switch pools entirely rather than merge them.
    /// </summary>
    public static List<PotTreasureCandidate> BuildRerollPool(IZone zone) =>
        zone.GetRerollPotChestData()
            .Select((chest, i) => new PotTreasureCandidate($"R{i + 1}", chest.Position, chest.Level))
            .ToList();

    /// <summary>Smart mode needs authored chest spots to narrow; otherwise it can only sweep.</summary>
    public static bool CanRunSmart(IZone zone, int fateId) =>
        zone.IsPotFate(fateId) && zone.GetPotChestData().ContainsKey(fateId);

    /// <summary>
    ///     Every authored spot for this pot FATE, as filter input.
    ///     Reroll pads are deliberately excluded. They sit in remote second-chance areas — 250y from
    ///     the nearest pot spot on average in North Horn, up to 515y — reached by aethernet rather
    ///     than on foot. Narrowing onto one would send the farm walking overland for the rest of the
    ///     buff. The blind sweep still visits them; it orders by distance and is a last resort.
    /// </summary>
    public static List<PotTreasureCandidate> BuildPool(IZone zone, int fateId)
    {
        if (!zone.GetPotChestData().TryGetValue(fateId, out List<PotChestData>? chests))
        {
            return [];
        }

        return chests
            .Select((chest, i) => new PotTreasureCandidate($"P{i + 1}", chest.Position, chest.Level))
            .ToList();
    }
}
