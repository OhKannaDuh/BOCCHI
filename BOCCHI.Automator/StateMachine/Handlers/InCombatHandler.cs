using BOCCHI.Automator.Data;
using BOCCHI.Common.Data.StateMemory;
using BOCCHI.Common.Services;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using ECommons.Throttlers;
using Ocelot.Actions;
using Ocelot.Services.Pathfinding;
using Ocelot.States.Score;

namespace BOCCHI.Automator.StateMachine.Handlers;

public class InCombatHandler
(
    IObjectTable objects,
    ICondition conditions,
    IFateContext fateContext,
    ICriticalEncounterContext criticalEncounterContext,
    IPathfinder pathfinder,
    IAutomatorMemory memory
) : ScoreStateHandler<AutomatorState, StatePriority>(AutomatorState.InCombat)
{
    public override StatePriority GetScore()
    {
        if (objects.LocalPlayer == null)
        {
            return StatePriority.Never;
        }

        if (criticalEncounterContext.IsInCriticalEncounter() || fateContext.IsInFate())
        {
            return StatePriority.Never;
        }

        // Pot chest farming also scores High, and this handler only dismounts and stops the
        // pathfinder — so winning the tie just interrupts the farm on every combat flicker.
        if (memory.TryRemember<PotChestFarmMemory>(out PotChestFarmMemory _)
            || memory.TryRemember<PendingPotChestFarmMemory>(out PendingPotChestFarmMemory _))
        {
            return StatePriority.Never;
        }

        if (memory.TryRemember<GoalPathStepMemory>(out GoalPathStepMemory _))
        {
            return StatePriority.Never;
        }

        return conditions[ConditionFlag.InCombat] ? StatePriority.High : StatePriority.Never;
    }

    public override void Handle()
    {
        if (objects.LocalPlayer is null)
        {
            return;
        }

        if (conditions[ConditionFlag.Mounted])
        {
            if (EzThrottler.Throttle("InCombat::Unmount") && Actions.Unmount.CanCast())
            {
                Actions.Unmount.Cast();
                pathfinder.Stop();
            }
        }
    }
}
