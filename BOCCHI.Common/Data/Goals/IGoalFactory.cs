using BOCCHI.Common.Data.CriticalEncounters;
using BOCCHI.Common.Data.Fates;
using BOCCHI.Common.Data.Goals;

namespace BOCCHI.Automator.Data.Goals;

public interface IGoalFactory
{
    IGoal Fate(FateId id);

    IGoal CriticalEncounter(CriticalEncounterId id);
}
