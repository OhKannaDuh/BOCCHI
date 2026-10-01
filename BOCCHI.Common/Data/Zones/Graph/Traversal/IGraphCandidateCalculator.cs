using Ocelot.Services.Pathfinding;
using System.Numerics;

namespace BOCCHI.Common.Data.Zones.Graph.Traversal;

public interface IGraphCandidateCalculator
{
    string Key();

    float? LowerBoundCost(ZoneGraph graph, Vector3 start, Node goal) => null;

    Task<TraversalCandidate?> CalculateAsync(ZoneGraph graph, Vector3 start, Node goal, IPathfinder pathfinder);
}
