using Ocelot.Extensions;
using Ocelot.Ipc.VNavmesh;
using System.Numerics;

namespace BOCCHI.Treasure.Services;

public static class TreasurePathing
{
    public const float UnloadAltitudeMax = -250f;

    private const float SnapExtentXZ = 8f;

    private const float SnapExtentY = 30f;

    private const float MaxSnapDeltaY = 25f;

    public static bool IsUnloadAltitude(float y) => y < UnloadAltitudeMax;

    public static bool IsUnloadAltitude(Vector3 position) => IsUnloadAltitude(position.Y);

    public static Vector3 PathablePosition(Vector3 position, float playerY)
    {
        if (IsUnloadAltitude(position.Y) || MathF.Abs(position.Y + 500f) < 0.5f)
        {
            return position with { Y = playerY };
        }

        return position;
    }

    public static bool TrySnapToNavmesh(
        Vector3 position,
        float playerY,
        IVNavmeshIpc vnav,
        out Vector3 pathable)
    {
        pathable = PathablePosition(position, playerY);
        if (!vnav.IsAvailable() || !vnav.IsNavmeshReady())
        {
            return true;
        }

        if (TrySnap(vnav, pathable, out Vector3 snapped) && IsNearSeed(pathable, snapped))
        {
            pathable = snapped;
            return true;
        }

        // No polygon near the authored altitude. Same-floor only — using the player's Y while they
        // stand under an island would snap the pad 50y down and loop (#201).
        if (MathF.Abs(pathable.Y - playerY) <= MaxSnapDeltaY)
        {
            Vector3 atPlayerAltitude = pathable with { Y = playerY };
            if (TrySnap(vnav, atPlayerAltitude, out snapped) && IsNearSeed(atPlayerAltitude, snapped))
            {
                pathable = snapped;
                return true;
            }
        }

        return false;
    }

    public static bool TryResolvePathable(
        Vector3 destination,
        float playerY,
        IVNavmeshIpc vnav,
        bool skipIfOffMesh,
        out Vector3 pathable)
    {
        if (TrySnapToNavmesh(destination, playerY, vnav, out pathable))
        {
            return true;
        }

        if (skipIfOffMesh)
        {
            return false;
        }

        pathable = PathablePosition(destination, playerY);
        return true;
    }

    private static bool IsNearSeed(Vector3 seed, Vector3 snapped) =>
        seed.Distance2D(snapped) <= SnapExtentXZ * 1.5f
        && MathF.Abs(seed.Y - snapped.Y) <= MaxSnapDeltaY;

    private static bool TrySnap(IVNavmeshIpc vnav, Vector3 seed, out Vector3 snapped)
    {
        snapped = seed;
        if (!vnav.TryFindPointOnMesh(seed, SnapExtentXZ, SnapExtentY, out Vector3 onMesh))
        {
            return false;
        }

        if (vnav.TryFindPointOnFloor(onMesh, SnapExtentXZ, out Vector3 floored)
            && IsNearSeed(seed, floored))
        {
            snapped = floored;
            return true;
        }

        snapped = onMesh;
        return true;
    }
}
