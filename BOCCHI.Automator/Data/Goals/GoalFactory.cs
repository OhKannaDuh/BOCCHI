using BOCCHI.Common.Data.CriticalEncounters;
using BOCCHI.Common.Data.Fates;
using BOCCHI.Common.Data.Goals;

namespace BOCCHI.Automator.Data.Goals;

public class GoalFactory : IGoalFactory
{
    public IGoal Fate(FateId id) =>
        new Goal
        {
            GoalType = new FateGoal(id)
        };

    public IGoal CriticalEncounter(CriticalEncounterId id) =>
        new Goal
        {
            GoalType = new CriticalEncounterGoal(id)
        };

    public IGoal ForkedTower(CriticalEncounterId id) =>
        new Goal
        {
            GoalType = new ForkedTowerGoal(id)
        };
}
