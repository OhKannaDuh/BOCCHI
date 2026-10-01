using BOCCHI.Common.Data.Paths;
using Ocelot.Extensions;
using Ocelot.Services.Pathfinding;
using System.Numerics;
using Path = Ocelot.Services.Pathfinding.Path;

namespace BOCCHI.Common.Data.Zones.Graph.Traversal;

public class DirectWalkCalculator : IGraphCandidateCalculator
{
    public string Key() => "DirectWalk";

    public float? LowerBoundCost(ZoneGraph graph, Vector3 start, Node goal)
    {
        float approachOffset = goal.Metadata is ActivityNodeMetadata { CombatRadius: > 0 } meta
            ? meta.CombatRadius
            : NavigationConstants.EventApproachMaxRadius;

        return MathF.Max(0f, start.Distance2D(goal.Position) - approachOffset);
    }

    public async Task<TraversalCandidate?> CalculateAsync(ZoneGraph graph, Vector3 start, Node goal, IPathfinder pathfinder)
    {
        Vector3 approach = NavigationApproach.ResolveActivityApproach(goal, start);
        Path path = await pathfinder.Pathfind(new(approach)
        {
            From = start,
            AllowFlying = false
        });

        if (!path.IsReachable())
        {
            return null;
        }

        return new(path.Distance, [PathStep.Pathfind(approach, NavigationConstants.EventArrivalRadius)]);
    }
}
