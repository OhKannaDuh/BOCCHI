using BOCCHI.Automator.Data;
using BOCCHI.Automator.Services;
using BOCCHI.Common.Data.Aethernet;
using BOCCHI.Common.Data.CriticalEncounters;
using BOCCHI.Common.Data.Fates;
using BOCCHI.Common.Data.Goals;
using BOCCHI.Common.Data.StateMemory;
using BOCCHI.Common.Data.Zones;
using BOCCHI.Common.Services;
using BOCCHI.Treasure.Services;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using ECommons.Throttlers;
using ECommonsPlayer = ECommons.GameHelpers.Player;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Ocelot.Actions;
using Ocelot.Extensions;
using Ocelot.Ipc.Lifestream;
using Ocelot.Ipc.VNavmesh;
using Ocelot.Services.Gate;
using Ocelot.Services.Logger;
using Ocelot.Services.Pathfinding;
using Ocelot.Services.PlayerState;
using Ocelot.States.Score;

namespace BOCCHI.Automator.StateMachine.Handlers;

public class ReturningHandler
(
    IAutomatorMemory memory,
    IAutomatorContext automator,
    IZoneProvider zones,
    ICondition conditions,
    IAddonLifecycle addons,
    IFateRepository fates,
    ICriticalEncounterRepository criticalEncounters,
    IPlayer player,
    IGateService gate,
    AutoRotationController autoRotation,
    ITreasureHunter hunter,
    IPathfinder pathfinder,
    IVNavmeshIpc vnav,
    ILifestreamIpc lifestream,
    ILogger<ReturningHandler> logger
) : ScoreStateHandler<AutomatorState, StatePriority>(AutomatorState.Returning)
{
    private static readonly TimeSpan CastAttemptBudget = TimeSpan.FromSeconds(45);

    public override StatePriority GetScore()
    {
        if (!automator.Enabled || !zones.GetZone().IsOccultCrescentZone())
        {
            return StatePriority.Never;
        }

        if (memory.TryRemember<ReturningStateMemory>(out ReturningStateMemory _))
        {
            return StatePriority.VeryHigh;
        }

        if (memory.TryRemember<NavigationInterruptedMemory>(out NavigationInterruptedMemory _))
        {
            return StatePriority.Never;
        }

        if (automator.IsPotsAndTreasure)
        {
            return StatePriority.Never;
        }

        if (conditions[ConditionFlag.Unconscious])
        {
            return StatePriority.Never;
        }

        if (TriageSession.IsActive(memory))
        {
            return StatePriority.Never;
        }

        if (IsIllegalModeMapHuntFillerActive())
        {
            return StatePriority.Never;
        }

        if (memory.TryRemember<PotChestFarmMemory>(out PotChestFarmMemory _)
            || memory.TryRemember<PendingPotChestFarmMemory>(out PendingPotChestFarmMemory _))
        {
            return StatePriority.Never;
        }

        if (memory.TryRemember<AutomaticTreasureSurveyMemory>(out AutomaticTreasureSurveyMemory survey)
            && survey.PendingSurvey
            && !zones.GetZone().IsInBasecamp())
        {
            return StatePriority.High;
        }

        if (!memory.TryRemember<IdleStateMemory>(out IdleStateMemory idle) || zones.GetZone().IsInBasecamp())
        {
            return StatePriority.Never;
        }

        if (IsNearActiveFateGoal())
        {
            return StatePriority.Never;
        }

        if (IsCommittedToCriticalEncounterGoal())
        {
            return StatePriority.Never;
        }

        if (!idle.IsReadyToReturn())
        {
            return StatePriority.Never;
        }

        return StatePriority.VeryLow;
    }

    public override void Enter()
    {
        base.Enter();
        autoRotation.DisableAi();
        pathfinder.Stop();
        vnav.Stop();
        AethernetTeleport.AbortIfBusy(lifestream);
        addons.RegisterListener(AddonEvent.PostSetup, "SelectYesno", SelectYesNoListener);

        // Triage (or anything else) can hold us off Returning for a long time while the latch's
        // QueuedAt keeps ticking — without a refresh the CastAttemptBudget expires the moment we
        // resume and we skip Return for Teleport. Keep remaining humanize delay; reset the budget.
        // Survey / opportunistic Sight Return never came from a path handoff — still arm a latch
        // so CastAttemptBudget can fire (otherwise "Return not ready" forever after FATE/CE).
        if (memory.TryRemember<ReturningStateMemory>(out ReturningStateMemory prior))
        {
            TimeSpan delay = prior.IsReadyToCast()
                ? TimeSpan.Zero
                : prior.CastDelay - prior.GetTimeQueued();
            if (delay < TimeSpan.Zero)
            {
                delay = TimeSpan.Zero;
            }

            memory.Forget<ReturningStateMemory>();
            memory.TryAdd(new ReturningStateMemory(delay));
            logger.Debug(
                "Enter Returning delay={Delay:F1}s surveyPending={Survey} mounted={Mounted} combat={Combat} pos={Pos:F0}",
                delay.TotalSeconds,
                memory.TryRemember<AutomaticTreasureSurveyMemory>(out AutomaticTreasureSurveyMemory s1) && s1.PendingSurvey,
                conditions[ConditionFlag.Mounted],
                conditions[ConditionFlag.InCombat],
                player.Position);
        }
        else
        {
            memory.TryAdd(new ReturningStateMemory(TimeSpan.Zero));
            logger.Debug(
                "Enter Returning delay=0s (fresh latch) surveyPending={Survey} mounted={Mounted} combat={Combat} pos={Pos:F0}",
                memory.TryRemember<AutomaticTreasureSurveyMemory>(out AutomaticTreasureSurveyMemory s2) && s2.PendingSurvey,
                conditions[ConditionFlag.Mounted],
                conditions[ConditionFlag.InCombat],
                player.Position);
        }
    }

    public override void Handle()
    {
        if (!automator.Enabled || !zones.GetZone().IsOccultCrescentZone())
        {
            memory.Forget<ReturningStateMemory>();
            return;
        }

        if (conditions[ConditionFlag.Unconscious])
        {
            memory.Forget<ReturningStateMemory>();
            return;
        }

        if (!gate.Milliseconds(this, "ReturningHandler::Gate", 500))
        {
            return;
        }

        bool isCasting = conditions[ConditionFlag.Casting] || conditions[ConditionFlag.Casting87];
        bool isBetweenAreas = conditions[ConditionFlag.BetweenAreas] || conditions[ConditionFlag.BetweenAreas51];

        if (isCasting || isBetweenAreas)
        {
            return;
        }

        IZone zone = zones.GetZone();
        if (zone.IsInBasecamp())
        {
            memory.Forget<ReturningStateMemory>();
            return;
        }

        if (TryConfirmReturnDialog())
        {
            return;
        }

        if (IsReturnDialogVisible())
        {
            return;
        }

        bool surveyLatch = memory.TryRemember<AutomaticTreasureSurveyMemory>(out AutomaticTreasureSurveyMemory latch)
                           && latch.PendingSurvey;
        if (memory.TryRemember<ReturningStateMemory>(out ReturningStateMemory returning))
        {
            if (!returning.IsReadyToCast() && !surveyLatch)
            {
                return;
            }

            TimeSpan budget = returning.CastDelay + CastAttemptBudget;
            if (returning.GetTimeQueued() >= budget)
            {
                OnReturnTimedOut(returning.GetTimeQueued());
                return;
            }
        }

        // Return is blocked in combat; holding VeryHigh forever left status on "Returning to camp"
        // while standing still (issue #178). Wait for combat to drop, then cast.
        if (conditions[ConditionFlag.InCombat])
        {
            if (EzThrottler.Throttle("ReturningHandler::Combat", 5000))
            {
                logger.Debug("Waiting for combat to end before Return");
            }

            return;
        }

        if (DismountAssist.TryDismount(conditions))
        {
            return;
        }

        if (ECommonsPlayer.IsJumping)
        {
            return;
        }

        if (IsOccupiedForReturn())
        {
            if (EzThrottler.Throttle("ReturningHandler::Occupied", 5000))
            {
                logger.Debug("Waiting to clear occupation before Return");
            }

            return;
        }

        unsafe
        {
            ActionManager* actions = ActionManager.Instance();
            if (actions != null && actions->AnimationLock > 0f)
            {
                return;
            }
        }

        if (OccultReturn.CanCast())
        {
            pathfinder.Stop();
            vnav.Stop();
            logger.Debug("Casting Occult Return to camp");
            OccultReturn.Cast();
            return;
        }

        if (EzThrottler.Throttle("ReturningHandler::CanCast", 5000))
        {
            LogReturnNotReady();
        }
    }

    private void OnReturnTimedOut(TimeSpan queued)
    {
        pathfinder.Stop();
        vnav.Stop();
        OccultReturn.Cast();

        if (EzThrottler.Throttle("ReturningHandler::Timeout", 5000))
        {
            logger.Warning(
                "Return to camp timed out after {Seconds:F0}s (combat/mount/cast blocked?) — continuing without Return",
                queued.TotalSeconds);
        }

        memory.Forget<ReturningStateMemory>();

        if (memory.TryRemember<AutomaticTreasureSurveyMemory>(out AutomaticTreasureSurveyMemory survey)
            && survey.PendingSurvey
            && !zones.GetZone().IsInBasecamp())
        {
            survey.PendingSurvey = false;
            survey.WaitingForSurveyResult = false;
            survey.SurveyWaitDeadlineUtc = DateTime.MinValue;
            survey.PendingMapHunt = true;
            logger.Warning(
                "Treasure Sight survey aborted — Return stayed blocked; map treasure hunt will resume from the field");
        }
    }

    private bool IsOccupiedForReturn() =>
        conditions[ConditionFlag.Occupied]
        || conditions[ConditionFlag.OccupiedInEvent]
        || conditions[ConditionFlag.OccupiedInQuestEvent]
        || conditions[ConditionFlag.OccupiedInCutSceneEvent]
        || conditions[ConditionFlag.Occupied39];

    private unsafe void LogReturnNotReady()
    {
        ActionManager* actions = ActionManager.Instance();
        float recast = Actions.Return.GetRecastTime();
        uint status = actions != null
            ? actions->GetActionStatus(Actions.Return.Type, Actions.Return.Id)
            : uint.MaxValue;
        float animLock = actions != null ? actions->AnimationLock : -1f;

        logger.Debug(
            "Occult Return not ready (mounted={Mounted}, combat={Combat}, jumping={Jumping}, occupied={Occupied}, status={Status}, overworldRecast={Recast:F2}, animLock={AnimLock:F2})",
            DismountAssist.IsMounted(conditions),
            conditions[ConditionFlag.InCombat],
            ECommonsPlayer.IsJumping,
            IsOccupiedForReturn(),
            status,
            recast,
            animLock);
    }

    public override void Exit(AutomatorState next)
    {
        base.Exit(next);

        memory.Forget<IdleStateMemory>();
        addons.UnregisterListener(AddonEvent.PostSetup, "SelectYesno", SelectYesNoListener);
    }

    private unsafe void SelectYesNoListener(AddonEvent ev, AddonArgs args)
    {
        if (!automator.Enabled
            || !zones.GetZone().IsOccultCrescentZone()
            || conditions[ConditionFlag.Unconscious])
        {
            return;
        }

        ReturnYesNo.TryAccept((AtkUnitBase*)args.Addon.Address);
    }

    private unsafe bool TryConfirmReturnDialog()
    {
        if (!automator.Enabled || !zones.GetZone().IsOccultCrescentZone())
        {
            return false;
        }

        if (!AddonHelpers.TryGetSelectYesno(out AddonSelectYesno* yesno))
        {
            return false;
        }

        return ReturnYesNo.TryAccept(&yesno->AtkUnitBase);
    }

    private unsafe bool IsReturnDialogVisible()
    {
        if (!AddonHelpers.TryGetSelectYesno(out AddonSelectYesno* yesno))
        {
            return false;
        }

        return ReturnYesNo.IsReturnConfirmation(&yesno->AtkUnitBase);
    }

    private bool IsIllegalModeMapHuntFillerActive()
    {
        if (hunter.ManagedByIllegalModeFiller && hunter.Running && !hunter.Paused)
        {
            return true;
        }

        return memory.TryRemember<AutomaticTreasureSurveyMemory>(out AutomaticTreasureSurveyMemory survey)
               && survey.PendingMapHunt;
    }

    private bool IsNearActiveFateGoal()
    {
        if (!memory.TryRemember<GoalMemory>(out GoalMemory goal) || goal.Goal.GoalType is not FateGoal fateGoal)
        {
            return false;
        }

        Fate? fate = fates.Snapshot().FirstOrDefault(f => f.Id.Value == fateGoal.id.Value);
        if (fate == null)
        {
            return false;
        }

        float radius = fate.Radius > 0f
            ? fate.Radius * 0.9f
            : NavigationConstants.EventArrivalRadius;
        return player.Position.Distance2D(fate.Position) <= radius;
    }

    private bool IsCommittedToCriticalEncounterGoal()
    {
        if (memory.TryRemember<WaitingForCriticalEncounterMemory>(out WaitingForCriticalEncounterMemory _)
            || memory.TryRemember<SuspendTravelForActivityMemory>(out SuspendTravelForActivityMemory _))
        {
            return true;
        }

        if (!memory.TryRemember<GoalMemory>(out GoalMemory goal)
            || goal.Goal.GoalType is not CriticalEncounterGoal ceGoal)
        {
            return false;
        }

        CriticalEncounter? ce = criticalEncounters.SnapshotWithoutForkedTower()
            .FirstOrDefault(c => c.Id == ceGoal.id);
        return ce is { } encounter && (encounter.IsPreparing() || encounter.IsActive());
    }
}
