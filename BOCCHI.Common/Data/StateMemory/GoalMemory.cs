using BOCCHI.Common.Data.Goals;

namespace BOCCHI.Common.Data.StateMemory;

public sealed class GoalMemory(IGoal goal)
{
    public IGoal Goal { get; } = goal;
}
