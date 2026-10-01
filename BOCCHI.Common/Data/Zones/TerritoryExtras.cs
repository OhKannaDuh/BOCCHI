using System.Numerics;

namespace BOCCHI.Common.Data.Zones;

public readonly record struct BuffZone(Vector3 Center, float RadiusMin, float RadiusMax)
{
    public bool IsWithinCastRadius2D(Vector3 position) => Distance2D(position) <= RadiusMax;

    private float Distance2D(Vector3 position)
    {
        float dx = position.X - Center.X;
        float dz = position.Z - Center.Z;
        return MathF.Sqrt((dx * dx) + (dz * dz));
    }

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

public sealed class TreasureRoutePolicy
{
    public IReadOnlyList<uint> UnsafeWeatherIds { get; init; } = [];

    public int AshkinStartEorzeaMinute { get; init; } = -1;

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

        return eorzeaMinuteOfDay >= AshkinStartEorzeaMinute || eorzeaMinuteOfDay < AshkinEndEorzeaMinute;
    }

    public static int GetEorzeaMinuteOfDay(DateTimeOffset utc)
    {
        long eorzeaSeconds = utc.ToUnixTimeSeconds() * 3600L / 175L;
        return (int)((eorzeaSeconds % 86400L) / 60L);
    }
}
