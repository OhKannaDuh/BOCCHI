using System.Numerics;

namespace BOCCHI.Common.Data.Zones;

/// <summary>Disk around the knowledge crystal for buff casting (approach uses RadiusMin).</summary>
public readonly record struct BuffZone(Vector3 Center, float RadiusMin, float RadiusMax)
{
    /// <summary>True inside the outer buff radius (includes standing on the crystal).</summary>
    public bool IsWithinCastRadius2D(Vector3 position) => Distance2D(position) <= RadiusMax;

    private float Distance2D(Vector3 position)
    {
        float dx = position.X - Center.X;
        float dz = position.Z - Center.Z;
        return MathF.Sqrt((dx * dx) + (dz * dz));
    }

    /// <summary>Point on the inner ring toward the player (stand here to buff).</summary>
    public Vector3 GetApproachPoint(Vector3 from)
    {
        float dx = from.X - Center.X;
        float dz = from.Z - Center.Z;
        float len = MathF.Sqrt((dx * dx) + (dz * dz));
        if (len < 0.001f)
        {
            return new Vector3(Center.X + RadiusMin, Center.Y, Center.Z);
        }

        float scale = RadiusMin / len;
        return new Vector3(Center.X + (dx * scale), Center.Y, Center.Z + (dz * scale));
    }
}

/// <summary>Visible coffer / treasure-route safety + area→aethernet hints.</summary>
public sealed class TreasureRoutePolicy
{
    public IReadOnlyList<uint> UnsafeWeatherIds { get; init; } = [];

    /// <summary>Eorzea minute-of-day when Ashkin window starts (inclusive).</summary>
    public int AshkinStartEorzeaMinute { get; init; } = -1;

    /// <summary>Eorzea minute-of-day when Ashkin window ends (exclusive); may wrap midnight.</summary>
    public int AshkinEndEorzeaMinute { get; init; } = -1;

    public bool HasAshkinWindow => AshkinStartEorzeaMinute >= 0 && AshkinEndEorzeaMinute >= 0;

    public bool IsUnsafeWeather(uint weatherId) => UnsafeWeatherIds.Contains(weatherId);

    public bool IsAshkinPeriod(int eorzeaMinuteOfDay)
    {
        if (!HasAshkinWindow)
        {
            return false;
        }

        if (AshkinStartEorzeaMinute <= AshkinEndEorzeaMinute)
        {
            return eorzeaMinuteOfDay >= AshkinStartEorzeaMinute && eorzeaMinuteOfDay < AshkinEndEorzeaMinute;
        }

        // Wraps midnight (e.g. 1350 → 240).
        return eorzeaMinuteOfDay >= AshkinStartEorzeaMinute || eorzeaMinuteOfDay < AshkinEndEorzeaMinute;
    }

    /// <summary>Eorzea minute-of-day from Unix epoch.</summary>
    public static int GetEorzeaMinuteOfDay(DateTimeOffset utc)
    {
        long eorzeaSeconds = utc.ToUnixTimeSeconds() * 3600L / 175L;
        return (int)((eorzeaSeconds % 86400L) / 60L);
    }
}
