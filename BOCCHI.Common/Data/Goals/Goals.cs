using BOCCHI.Common.Data.CriticalEncounters;
using BOCCHI.Common.Data.Fates;

namespace BOCCHI.Common.Data.Goals;

public interface IGoal
{
    GoalType GoalType { get; }

    string Describe();
}

public interface IGoalFactory
{
    IGoal Fate(FateId id);

    IGoal CriticalEncounter(CriticalEncounterId id);

    IGoal ForkedTower(CriticalEncounterId id);
}

public abstract record GoalType;
public sealed record FateGoal(FateId id) : GoalType;
public sealed record CriticalEncounterGoal(CriticalEncounterId id) : GoalType;
public sealed record ForkedTowerGoal(CriticalEncounterId id) : GoalType;

public class Goal : IGoal
{
    public required GoalType GoalType { get; init; }

    public string Describe()
    {
        return GoalType switch
        {
            FateGoal(var id) => $"Fate: {id}",
            CriticalEncounterGoal(var id) => $"Critical Encounter: {id}",
            ForkedTowerGoal(var id) => $"Forked Tower: {id}",
            var _ => throw new ArgumentOutOfRangeException(nameof(GoalType))
        };
    }
}

