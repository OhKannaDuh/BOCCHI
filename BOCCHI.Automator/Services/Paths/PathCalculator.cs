using BOCCHI.Common.Data.Aethernet;
using BOCCHI.Common.Config;
using BOCCHI.Common.Data.CriticalEncounters;
using BOCCHI.Common.Data.Fates;
using BOCCHI.Common.Data.Goals;
using BOCCHI.Common.Data.Paths;
using BOCCHI.Common.Data.Zones;
using BOCCHI.Common.Data.Zones.Graph;
using BOCCHI.Common.Data.Zones.Graph.Traversal;
using BOCCHI.Common.Services;
using BOCCHI.Common.Services.Paths;
using Dalamud.Plugin.Services;
using Ocelot.Extensions;
using Ocelot.Services.Logger;
using Ocelot.Services.Pathfinding;
using System.Numerics;

namespace BOCCHI.Automator.Services.Paths;

public class PathCalculator
(
    IPathfinder pathfinder,
    IObjectTable objects,
    IZoneProvider zones,
    IFateRepository fates,
    ICriticalEncounterRepository criticalEncounters,
    IFateContext fateContext,
    AutomatorConfig config,
    ILogger<PathCalculator> logger
) : IPathCalculator
{
    public async Task<Queue<IPathStep>> Calculate(IGoal goal)
    {
        if (objects.LocalPlayer is not { } player)
        {
            logger.Warn("No Player");
            return [];
        }

        IZone zone = zones.GetZone();
        if (!zone.IsOccultCrescentZone())
        {
            logger.Warn("In wrong zone");
            return [];
        }

        // Already registered in the target FATE — no more travel needed.
        if (goal.GoalType is FateGoal fateGoal && fateContext.GetFateId() == fateGoal.id)
        {
            logger.Debug("Already inside target FATE.");
            return [];
        }

        ZoneGraph graph = await zone.GetGraph();

        Node goalNode;
        try
        {
            goalNode = GetGoalNode(goal, graph);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            logger.Error(ex.Message);
            return [];
        }

        // Prefer live FATE center when available. Square CEs prefer live LGB center (#122);
        // circular CEs keep authored graph staging (live markers can sit under elevated CEs).
        Node pathGoal = goalNode;
        Vector3? potPrepositionStandOff = null;
        if (goal.GoalType is FateGoal liveFateGoal
            && fates.Snapshot().FirstOrDefault(f => f.Id.Value == liveFateGoal.id.Value) is { } liveFate)
        {
            pathGoal = new Node
            {
                Id = goalNode.Id,
                Type = goalNode.Type,
                Position = liveFate.Position,
                Metadata = goalNode.Metadata
            };
        }
        else if (goal.GoalType is CriticalEncounterGoal liveCeGoal
                 && criticalEncounters.Snapshot()
                     .FirstOrDefault(c => c.Id.Value == liveCeGoal.id.Value) is { AreaShape: ActivityAreaShape.Square } liveCe)
        {
            pathGoal = new Node
            {
                Id = goalNode.Id,
                Type = goalNode.Type,
                Position = liveCe.Position,
                Metadata = goalNode.Metadata
            };
        }
        else if (goal.GoalType is FateGoal potPreposition
                 && zone.IsPotFate(potPreposition.id.Value))
        {
            potPrepositionStandOff = NavigationApproach.GetPotPrepositionPosition(goalNode.Position, player.Position);
        }

        float ceCombatRadius = 0f;
        ActivityAreaShape ceShape = ActivityAreaShape.Circle;
        if (goal.GoalType is CriticalEncounterGoal ceGoalForRadius)
        {
            ActivityData? authored = zone.GetCriticalEncounterData()
                .FirstOrDefault(a => a.Id == ceGoalForRadius.id.Value);
            ceCombatRadius = authored?.CombatRadius ?? 0f;
            ceShape = authored?.AreaShape ?? ActivityAreaShape.Circle;
        }

        Vector3 arrivalCheck = potPrepositionStandOff ?? pathGoal.Position;
        float distanceToGoal = player.Position.Distance2D(arrivalCheck);
        if (ceCombatRadius > 0f)
        {
            if (NavigationConstants.IsInsideCriticalEncounterWaitArea(
                    arrivalCheck,
                    ceCombatRadius,
                    ceShape,
                    player.Position))
            {
                logger.Debug("Inside CE wait area.");
                return [];
            }
        }
        else if (distanceToGoal <= NavigationConstants.EventArrivalRadius)
        {
            logger.Debug("Too close to destination.");
            return [];
        }

        GraphTraverser traverser = new(graph, pathfinder, logger);
        // Teleport-first: from camp this is usually instant (no vnav). DirectWalk only for short hops.
        if (!config.DisableTeleportWalkPathing)
        {
            traverser.AddCalculator(new WalkTeleportWalkCalculator());
        }

        traverser.AddCalculator(new DirectWalkCalculator());

        // Don't offer Return when already closer to the goal than to camp — that caused #84 loops.
        float distToCamp = graph.GetBaseCampAetheryteNode() is { } camp
            ? player.Position.Distance2D(camp.Position)
            : float.MaxValue;
        bool nearCriticalEncounter = ceCombatRadius > 0f
                                     && NavigationConstants.IsInsideCriticalEncounterWaitArea(
                                         arrivalCheck,
                                         ceCombatRadius,
                                         ceShape,
                                         player.Position);
        if (!nearCriticalEncounter
            && distanceToGoal > NavigationConstants.MaxDirectWalkDistance
            && distanceToGoal >= distToCamp * 0.5f)
        {
            traverser.AddCalculator(new ReturnTeleportWalkCalculator());
        }

        List<PathStep> steps = await traverser.FindPath(player.Position, pathGoal);
        List<PathStep> resolvedSteps = steps
            .Select(step => AethernetNavigation.ResolveAetherytePathStep(step, zone))
            .ToList();

        if (potPrepositionStandOff is { } standOff)
        {
            // Replace approach offsets with the random stand-off so bots don't stack on pot center.
            resolvedSteps = resolvedSteps
                .Select(step => step.PathStepData is Pathfind ? PathStep.Pathfind(standOff) : step)
                .ToList();
        }

        if (config.StopAfterReturn)
        {
            // Keep Return / Teleport; drop the walk to the FATE or CE (#139).
            resolvedSteps = resolvedSteps
                .Where(step => step.Kind != PathStepKind.Pathfind)
                .ToList();
            logger.Debug("TeleportOnlyTravel: {Count} step(s) after dropping pathfinds", resolvedSteps.Count);
        }

        return new(resolvedSteps);
    }

    private Node GetGoalNode(IGoal goal, ZoneGraph graph)
    {
        return goal.GoalType switch
        {
            CriticalEncounterGoal(var id) => GetActivityNode(id.Value, graph, NodeType.CriticalEncounter),
            FateGoal(var id) => GetActivityNode(id.Value, graph, NodeType.NormalFate, NodeType.PotFate),
            var _ => throw new ArgumentOutOfRangeException()
        };
    }

    private Node GetActivityNode(int id, ZoneGraph graph, params NodeType[] types)
    {
        List<Node> nodes = graph.GetNodesByTypes(types).Where(n =>
        {
            if (n.Metadata is not ActivityNodeMetadata meta)
            {
                return false;
            }

            return meta.Id == id;
        }).ToList();

        return nodes.Count == 0 ? throw new("No nodes for Activity") : nodes.First();
    }
}
