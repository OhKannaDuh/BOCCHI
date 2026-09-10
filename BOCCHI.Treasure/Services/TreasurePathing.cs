using Ocelot.Extensions;
using Ocelot.Ipc.VNavmesh;
using System.Numerics;

namespace BOCCHI.Treasure.Services;

/// <summary>Normalize coffer positions that the game exposes with bogus altitudes.</summary>
public static class TreasurePathing
{
    /// <summary>
    ///     Below this Y is unload / inside-floor junk (−500 to −980), not hamlet basement (~−162).
    ///     Keep in sync with coffer-api <c>worldBounds.ts</c> <c>MIN_VALID_WORLD_Y</c>.
    /// </summary>
    public const float UnloadAltitudeMax = -250f;

    /// <summary>Horizontal slack when snapping an authored pad onto the navmesh.</summary>
    private const float SnapExtentXZ = 8f;

    /// <summary>
    ///     Vertical search around the authored / live Y. Wide enough for map Y being a bit off;
    ///     tight enough that an island pad cannot snap to the ground 50y below (#201 / #176).
    /// </summary>
    private const float SnapExtentY = 30f;

    /// <summary>Reject a snap that changed floors — stacked geometry (island over hamlet).</summary>
    private const float MaxSnapDeltaY = 25f;

    public static bool IsUnloadAltitude(float y) => y < UnloadAltitudeMax;

    public static bool IsUnloadAltitude(Vector3 position) => IsUnloadAltitude(position.Y);

    /// <summary>Rewrite unload / Y ≈ -500 reveal altitudes. Do not snap authored pads to the player's Y.</summary>
    public static Vector3 PathablePosition(Vector3 position, float playerY)
    {
        if (IsUnloadAltitude(position.Y) || MathF.Abs(position.Y + 500f) < 0.5f)
        {
            return position with { Y = playerY };
        }

        return position;
    }

    /// <summary>
    ///     Project a coffer / pad onto the navmesh. Returns false when vnav has no polygon
    ///     (airborne authored Y, void) — callers must not PathfindAndMoveTo that point.
    ///     When the mesh is not ready, returns true with the unsnapped position.
    /// </summary>
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

    /// <summary>
    ///     Mesh point we walk to. Authored pads with no same-floor polygon are skipped when
    ///     <paramref name="skipIfOffMesh"/> is set; live coffers still get a Y rewrite on failure.
    /// </summary>
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

    /// <summary>
    ///     Nearest-mesh can land on a cliff or the floor under an island. That is not this coffer.
    /// </summary>
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

        // Floor snap can drop through a hole onto the storey below; keep the mesh point then.
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
