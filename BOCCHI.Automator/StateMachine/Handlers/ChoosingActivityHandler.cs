using BOCCHI.Automator.Data;
using BOCCHI.Automator.Data.Goals;
using BOCCHI.Automator.Data.StateMemory;
using BOCCHI.Automator.Services;
using BOCCHI.Common.Data.StateMemory;
using BOCCHI.Common.Services;
using FFXIVClientStructs.FFXIV.Client.Game.InstanceContent;
using Ocelot.States.Score;

namespace BOCCHI.Automator.StateMachine.Handlers;

public class ChoosingActivityHandler(
    IAutomatorMemory memory,
    ICriticalEncounterRepository criticalEncounterRepository,
    IFateRepository fateRepository,
    IGoalFactory goalFactory
) : ScoreStateHandler<AutomatorState, StatePriority>(AutomatorState.ChoosingActivity)
{
    public override StatePriority GetScore()
    {
        if (memory.TryRemember<GoalMemory>(out var _))
        {
            return StatePriority.Never;
        }

        var fates = fateRepository.Snapshot().Count;
        // var criticalEncounters = criticalEncounterRepository.SnapshotWithoutForkedTower().Count(ce => ce.State == DynamicEventState.Register);
        var criticalEncounters = 0;

        if (fates <= 0 && criticalEncounters <= 0)
        {
            return StatePriority.Never;
        }

        return StatePriority.VeryLow;
    }

    public override void Handle()
    {
        // var criticalEncounter = criticalEncounterRepository.SnapshotWithoutForkedTower().FirstOrDefault(c => c.State == DynamicEventState.Register);
        // if (criticalEncounter != null)
        // {
        //     var goal = goalFactory.CriticalEncounter(criticalEncounter.Id);
        //     memory.TryAdd(new GoalMemory(goal));
        //     return;
        // }


        // @TODO: We can design a fate scoring system later
        var fates = fateRepository.Snapshot();
        var fate = fates.FirstOrDefault();
        if (fate != null)
        {
            var goal = goalFactory.Fate(fate.Id);
            memory.TryAdd(new GoalMemory(goal));
            return;
        }

        throw new Exception("Unable to determine a goal in the Choosing activity state...");
    }
}
