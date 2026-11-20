using BOCCHI.Automator.Data;
using BOCCHI.Automator.Services;
using BOCCHI.Common.Data.StateMemory;
using BOCCHI.Common.Data.Zones;
using BOCCHI.Common.Services;
using BOCCHI.Treasure.Services;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
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
    private const StatePriority AboveWaiting = StatePriority.VeryHigh + 1;

    private bool defendingWaitSpot;

    private bool aiEnabled;

    public override StatePriority GetScore()
    {
        if (objects.LocalPlayer == null || !conditions[ConditionFlag.InCombat])
        {
            defendingWaitSpot = false;
            return StatePriority.Never;
        }

        if (criticalEncounterContext.IsInCriticalEncounter() || fateContext.IsInFate())
        {
            return StatePriority.Never;
        }

        // Pot chest farming also scores High and runs its own fight-or-keep-searching logic.
        if (memory.TryRemember<PotChestFarmMemory>(out PotChestFarmMemory _)
            || memory.TryRemember<PendingPotChestFarmMemory>(out PendingPotChestFarmMemory _))
        {
            return StatePriority.Never;
        }

        // The pot wait forgets its memory on exit, so remember here or the two states flip every tick.
        defendingWaitSpot |= memory.TryRemember<WaitingForPotFateMemory>(out WaitingForPotFateMemory _)
            || memory.TryRemember<WaitingForCriticalEncounterMemory>(out WaitingForCriticalEncounterMemory _)
            || memory.TryRemember<WaitingForForkedTowerMemory>(out WaitingForForkedTowerMemory _);
        if (defendingWaitSpot)
        {
            return AboveWaiting;
        }

        if (memory.TryRemember<GoalPathStepMemory>(out GoalPathStepMemory _))
        {
            return KnowledgeThreat.CanFightAggro(objects) ? StatePriority.High + 1 : StatePriority.Never;
        }

        return StatePriority.High;
    }

    public override void Handle()
    {
        if (objects.LocalPlayer is null)
        {
            return;
        }

        pathfinder.Stop();
        if (DismountAssist.TryDismount(conditions) || aiEnabled)
        {
            return;
        }

        aiEnabled = true;
        autoRotation.EnableForSelfDefence();
    }

    public override void Enter()
    {
        base.Enter();
        aiEnabled = false;
    }

    public override void Exit(AutomatorState next)
    {
        base.Exit(next);
        defendingWaitSpot = false;
        if (aiEnabled)
        {
            aiEnabled = false;
            autoRotation.DisableAi();
        }
    }
}
