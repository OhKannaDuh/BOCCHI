using System.Numerics;

namespace BOCCHI.Automator.Services.Paths;

public static class PathMounting
{
    public static bool IsShortHopNearPot(Vector3 player, Vector3 destination, IEnumerable<Vector3> pots, float reach) =>
        Distance2D(player, destination) <= reach
        && pots.Any(pot => Distance2D(pot, destination) <= reach);

    private static float Distance2D(Vector3 a, Vector3 b) => Vector2.Distance(new(a.X, a.Z), new(b.X, b.Z));
}
