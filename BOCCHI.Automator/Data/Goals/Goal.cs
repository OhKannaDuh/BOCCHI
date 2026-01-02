using BOCCHI.Common.Data.Goals;

namespace BOCCHI.Automator.Data.Goals;

public class Goal : IGoal
{
    public required GoalType GoalType { get; init; }

    public string Describe()
    {
        return GoalType switch
        {
            FateGoal(var id) => $"Fate: {id}",
            CriticalEncounterGoal(var id) => $"Critical Encounter: {id}",
            _ => throw new ArgumentOutOfRangeException(nameof(GoalType))
        };
    }
}
