using System.Numerics;

namespace BOCCHI.Common.Data.Zones;

public static class PathfindingNudge
{
    public const float DefaultDistance = 8f;

    public static Vector3 LateralFrom(Vector3 from, Vector3 dest, float distance = DefaultDistance)
    {
        Vector3 toDest = dest - from;
        toDest.Y = 0f;
        if (toDest.LengthSquared() < 0.25f)
        {
            toDest = new Vector3(1f, 0f, 0f);
        }

        Vector3 forward = Vector3.Normalize(toDest);
        Vector3 lateral = new(-forward.Z, 0f, forward.X);
        Vector3 nudge = from + (lateral * distance);
        return new Vector3(nudge.X, from.Y, nudge.Z);
    }
}
