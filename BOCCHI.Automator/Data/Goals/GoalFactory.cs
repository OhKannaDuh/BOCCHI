using BOCCHI.Automator.Services.Paths;
using BOCCHI.Common.Data.CriticalEncounters;
using BOCCHI.Common.Data.Fates;
using BOCCHI.Common.Data.Goals;
using BOCCHI.Common.Services.Paths;

namespace BOCCHI.Automator.Data.Goals;

public class GoalFactory(IPathCalculator calculator) :  IGoalFactory
{
    public IGoal Fate(FateId id)
    {
        var type = new FateGoal(id);

        return new Goal {
            GoalType = type,
        };
    }

    public IGoal CriticalEncounter(CriticalEncounterId id)
    {
        var type = new CriticalEncounterGoal(id);

        return new Goal
        {
            GoalType = type,
        };
    }
}
