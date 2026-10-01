using BOCCHI.Automator.Data;
using BOCCHI.Automator.Services;
using BOCCHI.Common.Config;
using BOCCHI.Common.Data.CriticalEncounters;
using BOCCHI.Common.Data.Goals;
using BOCCHI.Common.Data.StateMemory;
using BOCCHI.Common.Data.Zones;
using BOCCHI.Common.Services;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using Ocelot.Pathfinding.Extensions;
using Ocelot.Services.Logger;
using Ocelot.Services.Pathfinding;
using Ocelot.Services.PlayerState;
using Ocelot.States.Score;

namespace BOCCHI.Automator.StateMachine.Handlers;

public class InCriticalEncounterHandler
(
    IAutomatorMemory memory,
    ICriticalEncounterContext context,
    ICriticalEncounterRepository repo,
    IObjectTable objects,
    ICondition conditions,
    IPathfinder pathfinder,
    AutoRotationController autoRotation,
    IPlayer playerState,
    AutomatorConfig config,
    ILogger<InCriticalEncounterHandler> logger
) : ScoreStateHandler<AutomatorState, StatePriority>(AutomatorState.InCriticalEncounter)
{
    public override StatePriority GetScore()
    {
        if (!memory.TryRemember<GoalMemory>(out GoalMemory goal)
            || goal.Goal.GoalType is not CriticalEncounterGoal ceGoal)
        {
            return StatePriority.Never;
        }

        if (memory.TryRemember<CommittedCriticalEncounterMemory>(out CommittedCriticalEncounterMemory committed)
            && committed.IsFor(ceGoal.id))
        {
            CriticalEncounter? latched = repo.SnapshotWithoutForkedTower()
                .FirstOrDefault(c => c.Id == ceGoal.id);
            if (latched is { } stillUp && stillUp.IsActive())
            {
                return StatePriority.VeryHigh;
            }
        }

        if (TryGetCommittedBattleEncounter(out _))
        {
            return StatePriority.VeryHigh;
        }

        if (context.GetCriticalEncounterId() != ceGoal.id)
        {
            return StatePriority.Never;
        }

        if (!config.CombatAutorotation.UsesCombatAutomation())
        {
            return StatePriority.VeryHigh;
        }

        if (objects.LocalPlayer is not { } player)
        {
            return StatePriority.Never;
        }

        CriticalEncounter? found = repo.SnapshotWithoutForkedTower().FirstOrDefault(c => c.Id == ceGoal.id);
        if (found is not { } ce || !ce.IsActive())
        {
            return StatePriority.Never;
        }

        float combatRadius = NavigationConstants.CriticalEncounterRedRadius(ce.Radius, ce.AreaShape);
        if (!NavigationConstants.IsInsideCriticalEncounterWaitArea(
                ce.RegistrationCenter,
                combatRadius,
                ce.AreaShape,
                player.Position))
        {
            return StatePriority.Never;
        }

        return StatePriority.VeryHigh;
    }

    public override void Enter()
    {
        base.Enter();
        memory.Forget<WaitingForCriticalEncounterMemory>();
        memory.Forget<CommittedCriticalEncounterMemory>();
        memory.TryAdd(new SuspendTravelForActivityMemory());
        memory.Forget<GoalPathStepMemory>();
        pathfinder.Stop();
        autoRotation.EnableForCriticalEncounter();
        ushort? ceId = context.GetCriticalEncounterId()?.Value;
        if (ceId == null
            && memory.TryRemember<GoalMemory>(out GoalMemory goal)
            && goal.Goal.GoalType is CriticalEncounterGoal ceGoal)
        {
            ceId = ceGoal.id.Value;
        }

        if (ceId is ushort entered)
        {
            memory.TryAdd(new CommittedCriticalEncounterMemory(new(entered)));
        }

        logger.Info("Entered CE {Id} — travel suspended", ceId?.ToString() ?? "?");
    }

    public override void Exit(AutomatorState next)
    {
        // Keep CE commitment across Dead so raise does not drop the goal as "still pathing".
        // Drop SuspendTravel first so DisableAi actually turns combat off while down.
        if (next == AutomatorState.Dead)
        {
            memory.Forget<SuspendTravelForActivityMemory>();
            autoRotation.DisableAi();
            logger.Info("Died in CE — keeping commitment for raise");
            base.Exit(next);
            return;
        }

        memory.Forget<SuspendTravelForActivityMemory>();
        memory.Forget<CommittedCriticalEncounterMemory>();
        autoRotation.DisableAi();
        logger.Info("Left CE — travel resumed");
        base.Exit(next);
    }

    public override void Handle()
    {
        if (objects.LocalPlayer is not { } player)
        {
            return;
        }

        if (config.CombatAutorotation.UsesCombatAutomation())
        {
            DismountAssist.TryDismount(conditions);
            if (!pathfinder.IsIdle())
            {
                pathfinder.Stop();
            }

            return;
        }

        List<IBattleNpc> targets = context.GetTargets().ToList();
        if (targets.Count == 0 && TryGetCommittedBattleEncounter(out CriticalEncounter committed))
        {
            targets = context.GetTargetsFor(committed.Id).ToList();
        }

        CombatActivityHandler.HandleTargets(
            player,
            playerState,
            targets,
            conditions,
            pathfinder,
            "InCriticalEncounter",
            shouldApproachTarget: false);
    }

    private bool TryGetCommittedBattleEncounter(out CriticalEncounter ce)
    {
        ce = null!;

        if (!memory.TryRemember<GoalMemory>(out GoalMemory goal)
            || goal.Goal.GoalType is not CriticalEncounterGoal ceGoal)
        {
            return false;
        }

        CriticalEncounter? found = repo.SnapshotWithoutForkedTower().FirstOrDefault(c => c.Id == ceGoal.id);
        if (found is not { } encounter || !encounter.IsActive())
        {
            return false;
        }

        if (memory.TryRemember<WaitingForCriticalEncounterMemory>(out WaitingForCriticalEncounterMemory wait)
            && wait.IsFor(encounter.Id)
            && objects.LocalPlayer is { } waitingPlayer
            && CriticalEncounterBattleHandoff.IsReady(encounter, context, waitingPlayer.Position))
        {
            ce = encounter;
            return true;
        }

        if (!memory.TryRemember<SuspendTravelForActivityMemory>(out SuspendTravelForActivityMemory _))
        {
            return false;
        }

        if (context.GetCriticalEncounterId() == encounter.Id
            || context.HasEncounterEnemies(encounter.Id))
        {
            ce = encounter;
            return true;
        }

        if (objects.LocalPlayer is not { } player)
        {
            return false;
        }

        float combatRadius = NavigationConstants.CriticalEncounterRedRadius(encounter.Radius, encounter.AreaShape);
        if (!NavigationConstants.IsInsideCriticalEncounterWaitArea(
                encounter.RegistrationCenter,
                combatRadius,
                encounter.AreaShape,
                player.Position))
        {
            return false;
        }

        ce = encounter;
        return true;
    }
}
