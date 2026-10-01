using BOCCHI.Common.Data.Goals;
using System.Numerics;
using BOCCHI.Common.Data.Paths;

namespace BOCCHI.Common.Services.Paths;

public interface IPathCalculator
{
    Task<PathCalculationResult> Calculate(IGoal goal);

    Task<PathCalculationResult> CalculateToPosition(Vector3 destination, float arrivalRange);
}

public readonly record struct PathCalculationResult(Queue<IPathStep> Steps, bool RoutingFailed = false)
{
    public static PathCalculationResult NoTravelNeeded() => new([]);

    public static PathCalculationResult Failed() => new([], RoutingFailed: true);

    public static PathCalculationResult Planned(IEnumerable<IPathStep> steps) => new(new Queue<IPathStep>(steps));
}
