using BOCCHI.Automator.Data.Goals;
using BOCCHI.Common.Data.Goals;
using BOCCHI.Common.Data.Paths;
using BOCCHI.Common.Services.Paths;

namespace BOCCHI.Automator.Data.StateMemory;

public sealed class GoalPathStepMemory(IGoal goal, IPathCalculator calculator)
{
    private Task<Queue<IPathStep>>? pathStepTask = calculator.Calculate(goal);

    public Queue<IPathStep> PathSteps { get; private set; } = [];

    public bool IsValid
    {
        get => pathStepTask != null || PathSteps.Count != 0;
    }

    public void Update()
    {
        if (pathStepTask != null)
        {
            if (pathStepTask.IsCompleted)
            {
                if (pathStepTask .IsCompletedSuccessfully) {
                    PathSteps = pathStepTask.Result;

                    pathStepTask.Dispose();
                    pathStepTask = null;
                }
            }
        }
    }

    public IPathStep? GetNextPathStep()
    {
        return PathSteps.Count > 0 && PathSteps.TryPeek(out var step) ? step : null;
    }

    public void DequeuePathStep()
    {
        if (PathSteps.Any())
        {
            PathSteps.Dequeue();
        }
    }
}
