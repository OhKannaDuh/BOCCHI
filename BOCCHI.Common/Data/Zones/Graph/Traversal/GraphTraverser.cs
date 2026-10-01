using BOCCHI.Common.Data.Paths;
using Ocelot.Services.Logger;
using Ocelot.Services.Pathfinding;
using System.Numerics;

namespace BOCCHI.Common.Data.Zones.Graph.Traversal;

public record TraversalCandidate(float TotalCost, List<PathStep> Steps);

public class GraphTraverser(ZoneGraph graph, IPathfinder pathfinder, ILogger logger)
{
    private readonly List<IGraphCandidateCalculator> calculators = [];

    public void AddCalculator(IGraphCandidateCalculator calculator)
    {
        calculators.Add(calculator);
    }

    public async Task<List<PathStep>> FindPath(Vector3 start, Node goal)
    {
        List<TraversalCandidate> candidates = new();

        List<(IGraphCandidateCalculator Calculator, float Bound)> deferred = [];

        foreach(IGraphCandidateCalculator calculator in calculators)
        {
            if (calculator.LowerBoundCost(graph, start, goal) is { } bound)
            {
                deferred.Add((calculator, bound));
                continue;
            }

            await Evaluate(calculator, start, goal, candidates);
        }

        foreach((IGraphCandidateCalculator calculator, float bound) in deferred)
        {
            float best = candidates.Count > 0
                ? candidates.Min(c => c.TotalCost)
                : float.PositiveInfinity;

            if (bound >= best)
            {
                logger.Debug(
                    $"Skipping Calculator: {calculator.Key()} (lower bound {bound:F0} >= best {best:F0})");
                continue;
            }

            await Evaluate(calculator, start, goal, candidates);
        }

        return candidates.MinBy(c => c.TotalCost)?.Steps ?? [];
    }

    private async Task Evaluate(
        IGraphCandidateCalculator calculator,
        Vector3 start,
        Node goal,
        List<TraversalCandidate> candidates)
    {
        logger.Debug($"Running Calculator: {calculator.Key()}");
        TraversalCandidate? candidate = await calculator.CalculateAsync(graph, start, goal, pathfinder);
        if (candidate != null)
        {
            logger.Debug($"  Cost:  {candidate.TotalCost}");
            candidates.Add(candidate);
        }
        else
        {
            logger.Debug("  Cost: N/A");
        }
    }
}
