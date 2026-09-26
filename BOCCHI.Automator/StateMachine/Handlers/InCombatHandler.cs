using BOCCHI.Automator.Data;
using BOCCHI.Automator.Services;
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
    IAutomatorMemory memory,
    AutoRotationController autoRotation
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

        // Pot chest farming also scores High and runs its own self-defence — winning the tie
        // just interrupts the farm on every combat flicker.
        if (memory.TryRemember<PotChestFarmMemory>(out PotChestFarmMemory _)
            || memory.TryRemember<PendingPotChestFarmMemory>(out PendingPotChestFarmMemory _))
        {
            return StatePriority.Never;
        }

        // Don't abandon Fate/CE transit to fight random trash on the road.
        if (memory.TryRemember<GoalPathStepMemory>(out GoalPathStepMemory _))
        {
            return StatePriority.Never;
        }

        return conditions[ConditionFlag.InCombat] ? StatePriority.High : StatePriority.Never;
    }

    public override void Enter()
    {
        base.Enter();
        // Open-world trash, or a fight BOCCHI lost track of (raised after the goal expired,
        // mode restarted mid-CE). Nothing else arms combat here, so without this we stand idle.
        autoRotation.EnableForSelfDefence();
    }

    public override void Exit(AutomatorState next)
    {
        autoRotation.DisableAi();
        base.Exit(next);
    }

    public override void Handle()
    {
        if (objects.LocalPlayer is null)
        {
            return;
        }

        // Open-world trash only — FATE/CE combat is InFate / InCriticalEncounter.

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
