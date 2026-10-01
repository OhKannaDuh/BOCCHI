using BOCCHI.Common.Data.Aethernet;
using BOCCHI.Common.Data.Paths;
using Ocelot.Extensions;
using Ocelot.Services.Pathfinding;
using System.Numerics;
using Path = Ocelot.Services.Pathfinding.Path;

namespace BOCCHI.Common.Data.Zones.Graph.Traversal;

public class WalkTeleportWalkCalculator : IGraphCandidateCalculator
{
    private const float GraphSnapRadius = 45f;

    public string Key() => "WalkTeleportWalk";

    public async Task<TraversalCandidate?> CalculateAsync(ZoneGraph graph, Vector3 start, Node goal, IPathfinder pathfinder)
    {
        IReadOnlyList<(Node Teleport, float Cost)> inbounds = graph.GetUsableInboundTeleports(goal);
        if (inbounds.Count == 0 || inbounds[0].Teleport.Metadata is not TeleportNodeMetadata inboundMeta)
        {
            return null;
        }

        Node inbound = inbounds[0].Teleport;
        float walkToGoalFromInbound = inbounds[0].Cost;

        (Node Departure, float WalkCost)? resolved = await ResolveDeparture(graph, start, pathfinder);
        if (resolved == null)
        {
            return null;
        }

        Node departure = resolved.Value.Departure;
        float walkToDepartureCost = resolved.Value.WalkCost;

        // Same inbound shard = no Lifestream hop on that pad. Short: walk straight.
        // Long: prefer teleporting to a *different* inbound shard (#172 Lost on the Wind),
        // otherwise walk via the pad (never a fake cross-map Pathfind that skips TP).
        if (IsSameAetheryte(departure, inbound, inboundMeta))
        {
            float direct = start.Distance2D(goal.Position);
            if (direct <= NavigationConstants.MaxDirectWalkDistance)
            {
                return BuildWalkOnly(start, goal, walkToDepartureCost + walkToGoalFromInbound);
            }

            if (TryBuildTeleportViaAlternateInbound(
                    graph,
                    start,
                    goal,
                    departure,
                    walkToDepartureCost,
                    out TraversalCandidate? viaOther)
                && viaOther != null)
            {
                return viaOther;
            }

            return BuildViaShardWalk(
                start,
                goal,
                departure,
                walkToDepartureCost + walkToGoalFromInbound);
        }

        return new(
            walkToDepartureCost + NavigationConstants.AethernetHopCost + walkToGoalFromInbound,
            BuildTeleportSteps(departure, inboundMeta.AetheryteId, goal, inbound, start));
    }

    private static async Task<(Node Departure, float WalkCost)?> ResolveDeparture(
        ZoneGraph graph,
        Vector3 start,
        IPathfinder pathfinder)
    {
        Node? baseCamp = graph.GetBaseCampAetheryteNode();
        if (baseCamp != null && start.Distance2D(baseCamp.Position) <= NavigationConstants.CampRadius)
        {
            return (baseCamp, start.Distance(baseCamp.Position));
        }

        if (graph.TryGetNode(start, GraphSnapRadius, out Node node) && node.IsTeleport())
        {
            return (node, start.Distance(node.Position));
        }

        Node? nearest = graph.GetNearestTeleport(start);
        if (nearest == null)
        {
            return null;
        }

        Vector3 approach = nearest.GetCampStandOffPosition(start);
        Path walkToNearestTeleportPath = await pathfinder.Pathfind(new(approach)
        {
            From = start,
            AllowFlying = false
        });

        if (!walkToNearestTeleportPath.IsReachable())
        {
            return null;
        }

        return (nearest, walkToNearestTeleportPath.Distance);
    }

    private static bool IsSameAetheryte(Node departure, Node inbound, TeleportNodeMetadata inboundMeta)
    {
        if (departure.Id == inbound.Id)
        {
            return true;
        }

        return departure.Metadata is TeleportNodeMetadata departureMeta
               && departureMeta.AetheryteId == inboundMeta.AetheryteId;
    }

    private static bool TryBuildTeleportViaAlternateInbound(
        ZoneGraph graph,
        Vector3 start,
        Node goal,
        Node departure,
        float walkToDepartureCost,
        out TraversalCandidate? candidate)
    {
        candidate = null;
        TraversalCandidate? best = null;

        foreach ((Node altInbound, float walkFromAlt) in graph.GetUsableInboundTeleports(goal))
        {
            if (altInbound.Metadata is not TeleportNodeMetadata altMeta)
            {
                continue;
            }

            if (IsSameAetheryte(departure, altInbound, altMeta))
            {
                continue;
            }

            float cost = walkToDepartureCost + NavigationConstants.AethernetHopCost + walkFromAlt;
            TraversalCandidate option = new(
                cost,
                BuildTeleportSteps(departure, altMeta.AetheryteId, goal, altInbound, start));
            if (best == null || option.TotalCost < best.TotalCost)
            {
                best = option;
            }
        }

        candidate = best;
        return best != null;
    }

    private static TraversalCandidate BuildWalkOnly(Vector3 start, Node goal, float cost) =>
        new(
            cost,
            [
                PathStep.Pathfind(
                    NavigationApproach.ResolveActivityApproach(goal, start),
                    NavigationConstants.EventArrivalRadius)
            ]);

    private static TraversalCandidate BuildViaShardWalk(
        Vector3 start,
        Node goal,
        Node departure,
        float cost)
    {
        List<PathStep> steps = [];
        Vector3 standOff = departure.GetCampStandOffPosition(start);
        float ready = departure.GetNodeLifestreamReadyRadius();
        float arrived = AethernetNavigation.PathfindArrivalRadius + AethernetNavigation.PathfindArrivalSlack;
        if (start.Distance2D(departure.Position) > ready
            && start.Distance2D(standOff) > arrived)
        {
            steps.Add(PathStep.Pathfind(standOff, AethernetNavigation.PathfindArrivalRadius));
        }

        Vector3 fromPad = departure.GetInteractPosition();
        steps.Add(PathStep.Pathfind(
            NavigationApproach.ResolveActivityApproach(goal, fromPad),
            NavigationConstants.EventArrivalRadius));
        return new(cost, steps);
    }

    private static List<PathStep> BuildTeleportSteps(
        Node departure,
        uint aetheryteId,
        Node goal,
        Node inbound,
        Vector3 start)
    {
        List<PathStep> steps = [];

        float ready = departure.GetNodeLifestreamReadyRadius();
        if (start.Distance2D(departure.Position) > ready)
        {
            Vector3 standOff = departure.GetCampStandOffPosition(start);
            float arrived = AethernetNavigation.PathfindArrivalRadius + AethernetNavigation.PathfindArrivalSlack;
            if (start.Distance2D(standOff) > arrived)
            {
                steps.Add(PathStep.Pathfind(standOff, AethernetNavigation.PathfindArrivalRadius));
            }
        }

        steps.Add(PathStep.Teleport(aetheryteId));
        steps.Add(PathStep.Pathfind(
            NavigationApproach.ResolveActivityApproach(goal, inbound.Position),
            NavigationConstants.EventArrivalRadius));
        return steps;
    }
}
