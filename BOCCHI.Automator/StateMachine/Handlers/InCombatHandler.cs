using BOCCHI.Automator.Data;
using BOCCHI.Automator.Services;
using BOCCHI.Common.Data.Goals;
using BOCCHI.Common.Data.StateMemory;
using BOCCHI.Common.Data.Zones;
using BOCCHI.Common.Services;
using BOCCHI.Treasure.Services;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using Ocelot.Services.Logger;
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
    AutoRotationController autoRotation,
    ILogger<InCombatHandler> logger
) : ScoreStateHandler<AutomatorState, StatePriority>(AutomatorState.InCombat)
{
    private const StatePriority AboveWaiting = StatePriority.VeryHigh + 1;

    private bool defendingWaitSpot;

    private bool aiEnabled;

    private bool loggedNotFighting;

    public override StatePriority GetScore()
    {
        if (objects.LocalPlayer == null || !conditions[ConditionFlag.InCombat])
        {
            defendingWaitSpot = false;
            loggedNotFighting = false;
            return StatePriority.Never;
        }

        // InFate / InCriticalEncounter only take over for the goal; yielding to any other event left nothing fighting.
        if (IsInGoalActivity())
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
            || memory.TryRemember<WaitingForForkedTowerMemory>(out WaitingForForkedTowerMemory _)
            || memory.TryRemember<TreasureHuntSelfDefenceMemory>(out TreasureHuntSelfDefenceMemory _);
        if (defendingWaitSpot)
        {
            return AboveWaiting;
        }

        if (memory.TryRemember<GoalPathStepMemory>(out GoalPathStepMemory _))
        {
            if (KnowledgeThreat.CanFightAggro(objects))
            {
                return StatePriority.High + 1;
            }

            if (!loggedNotFighting)
            {
                loggedNotFighting = true;
                logger.Info(
                    "Attacked while travelling by a level {Enemy} enemy (your Knowledge Level: {Mine}) — not stopping to fight",
                    Level(KnowledgeThreat.HighestAttackerLevel(objects)),
                    Level(KnowledgeThreat.TryGetPlayerForayLevel(objects)));
            }

            return StatePriority.Never;
        }

        return StatePriority.High;
    }

    private bool IsInGoalActivity() =>
        memory.TryRemember<GoalMemory>(out GoalMemory goal)
        && goal.Goal.GoalType switch
        {
            CriticalEncounterGoal ce => criticalEncounterContext.GetCriticalEncounterId() == ce.id,
            FateGoal fate => fateContext.GetFateId() == fate.id,
            _ => false,
        };

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
        logger.Info(
            "In combat: {Reason} (attacker level {Enemy}, your Knowledge Level {Mine})",
            memory.TryRemember<TreasureHuntSelfDefenceMemory>(out TreasureHuntSelfDefenceMemory _)
                ? "could not outrun it during the treasure hunt"
                : defendingWaitSpot ? "defending the waiting spot" : "fighting back",
            Level(KnowledgeThreat.HighestAttackerLevel(objects)),
            Level(KnowledgeThreat.TryGetPlayerForayLevel(objects)));
    }

    private static object Level(int? level) => level is { } value ? value : "?";

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
