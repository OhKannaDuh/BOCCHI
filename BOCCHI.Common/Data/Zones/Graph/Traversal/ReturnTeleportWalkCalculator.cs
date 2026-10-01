using BOCCHI.Common.Data.Aethernet;
using BOCCHI.Common.Data.Paths;
using Ocelot.Extensions;
using Ocelot.Services.Pathfinding;
using System.Numerics;

namespace BOCCHI.Common.Data.Zones.Graph.Traversal;

public class ReturnTeleportWalkCalculator : IGraphCandidateCalculator
{
    public string Key() => "ReturnTeleportWalk";

    public Task<TraversalCandidate?> CalculateAsync(ZoneGraph graph, Vector3 start, Node goal, IPathfinder pathfinder)
    {
        if (graph.TryGetNode(start, AethernetData.InteractRadius, out Node nearby) && nearby.IsTeleport())
        {
            return Task.FromResult<TraversalCandidate?>(null);
        }

        Node? baseCampAetheryte = graph.GetBaseCampAetheryteNode();
        Node? returnNode = graph.GetBaseCampReturnPositionNode();

        if (baseCampAetheryte != null && start.Distance2D(baseCampAetheryte.Position) <= NavigationConstants.CampRadius)
        {
            return Task.FromResult<TraversalCandidate?>(null);
        }

        if (returnNode != null && start.Distance2D(returnNode.Position) <= NavigationConstants.CampRadius)
        {
            return Task.FromResult<TraversalCandidate?>(null);
        }

        if (returnNode == null || baseCampAetheryte == null)
        {
            return Task.FromResult<TraversalCandidate?>(null);
        }

        Edge? toBaseCampNodeEdge = graph.GetEdge(returnNode, baseCampAetheryte);
        if (toBaseCampNodeEdge == null)
        {
            return Task.FromResult<TraversalCandidate?>(null);
        }

        IReadOnlyList<(Node Teleport, float Cost)> inbounds = graph.GetUsableInboundTeleports(goal);
        if (inbounds.Count == 0 || inbounds[0].Teleport.Metadata is not TeleportNodeMetadata meta)
        {
            return Task.FromResult<TraversalCandidate?>(null);
        }

        Node inbound = inbounds[0].Teleport;
        float walkToGoalFromInbound = inbounds[0].Cost;

        if (inbound.Type == NodeType.BaseCampAetheryte)
        {
            return Task.FromResult<TraversalCandidate?>(new(
                NavigationConstants.ReturnCost + toBaseCampNodeEdge.Cost + walkToGoalFromInbound,
                [
                    PathStep.Return(),
                    PathStep.Pathfind(
                        NavigationApproach.ResolveActivityApproach(goal, returnNode.Position),
                        NavigationConstants.EventArrivalRadius)
                ]));
        }

        return Task.FromResult<TraversalCandidate?>(new(
            NavigationConstants.ReturnCost + toBaseCampNodeEdge.Cost + NavigationConstants.AethernetHopCost + walkToGoalFromInbound,
            [
                PathStep.Return(),
                PathStep.Teleport(meta.AetheryteId),
                PathStep.Pathfind(
                    NavigationApproach.ResolveActivityApproach(goal, inbound.Position),
                    NavigationConstants.EventArrivalRadius)
            ]));
    }
}
