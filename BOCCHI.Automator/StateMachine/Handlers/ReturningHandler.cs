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
    /// <summary>
    ///     CastDelay can roll up to 60s; give dismount / combat drop / Yesno time after that.
    ///     Past this, drop the latch so Pathfinding can Teleport+Walk from the field instead of
    ///     standing on "Returning to camp" forever (Vertigo / #178-class hangs).
    /// </summary>
    private static readonly TimeSpan CastAttemptBudget = TimeSpan.FromSeconds(45);

    public override StatePriority GetScore()
    {
        if (!automator.Enabled || !zones.GetZone().IsOccultCrescentZone())
        {
            return StatePriority.Never;
        }

        // Treasure hunt is the idle filler in Pots & Treasure — never Return-to-camp.
        if (automator.IsPotsAndTreasure)
        {
            return StatePriority.Never;
        }

        // Return while dead accepts the death prompt and force-respawns.
        if (conditions[ConditionFlag.Unconscious])
        {
            return StatePriority.Never;
        }

        // Raise nearby players before leaving the FATE/CE site — keep the Return latch, but do
        // not score while triage is pending/active (otherwise VeryHigh hides triage + combat wait).
        if (TriageSession.IsActive(memory))
        {
            return StatePriority.Never;
        }

        // Pathfinding already dequeued Return — this latch must win even if a map hunt was
        // just latched, or Teleport starts from the field and Lifestream fires short of camp.
        if (memory.TryRemember<ReturningStateMemory>(out ReturningStateMemory _))
        {
            return StatePriority.VeryHigh;
        }

        // Map-hunt filler (no Treasure Sight): hunt owns opportunistic Return / routing while
        // actively moving. When paused for a FATE/CE, allow Automator Return (e.g. camp for buffs).
        if (IsIllegalModeMapHuntFillerActive())
        {
            return StatePriority.Never;
        }

        // Pot chest farm / deferred handoff — open the reveal before Sight Return.
        if (memory.TryRemember<PotChestFarmMemory>(out PotChestFarmMemory _)
            || memory.TryRemember<PendingPotChestFarmMemory>(out PendingPotChestFarmMemory _))
        {
            return StatePriority.Never;
        }

        // After activity, get to camp for Treasure Sight before the next CE/FATE.
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

        // Waiting inside / near the goal FATE circle — don't Return-to-base.
        if (IsNearActiveFateGoal())
        {
            return StatePriority.Never;
        }

        // Committed to a CE (wait latch / SuspendTravel / live Preparing|Battle goal) — never
        // Opportunistic Return while Goal still shows that CE (e.g. Familiar / Unbridled).
        if (IsCommittedToCriticalEncounterGoal())
        {
            return StatePriority.Never;
        }

        // Opportunistic Return while idle (OC has no Return CD). Keep below ChoosingActivity.
        return idle.IsReadyToReturn() ? StatePriority.VeryLow : StatePriority.Never;
    }

    public override void Enter()
    {
        base.Enter();
        autoRotation.DisableAi();
        // Movement / a mid-hop Lifestream cancel Return mid-cast.
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

        // Gate: true = interval elapsed (was inverted before).
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

        // Poll confirm — PostSetup alone can miss when BossMod slows UI setup.
        if (TryConfirmReturnDialog())
        {
            return;
        }

        if (IsReturnDialogVisible())
        {
            return;
        }

        // Path handoff: hold Returning while the rolled 2..max delay elapses.
        // Survey latch skips the humanize delay — get to camp for Sight ASAP.
        bool surveyLatch = memory.TryRemember<AutomaticTreasureSurveyMemory>(out AutomaticTreasureSurveyMemory latch)
                           && latch.PendingSurvey;
        if (memory.TryRemember<ReturningStateMemory>(out ReturningStateMemory returning))
        {
            if (!returning.IsReadyToCast() && !surveyLatch)
            {
                return;
            }

            // Latch spent too long without landing at camp — free Pathfinding to Teleport+Walk,
            // or drop a stuck Sight survey so Illegal Mode can map-hunt from the field.
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

        if (Actions.Return.CanCast())
        {
            pathfinder.Stop();
            vnav.Stop();
            logger.Debug("Casting Return to camp");
            Actions.Return.Cast();
            return;
        }

        if (EzThrottler.Throttle("ReturningHandler::CanCast", 5000))
        {
            LogReturnNotReady();
        }
    }

    private void OnReturnTimedOut(TimeSpan queued)
    {
        // Last try — CanCast can be overly strict after combat while UseAction still works.
        pathfinder.Stop();
        vnav.Stop();
        Actions.Return.Cast();

        if (EzThrottler.Throttle("ReturningHandler::Timeout", 5000))
        {
            logger.Warning(
                "Return to camp timed out after {Seconds:F0}s (combat/mount/cast blocked?) — continuing without Return",
                queued.TotalSeconds);
        }

        memory.Forget<ReturningStateMemory>();

        // Survey Return never had a Teleport path behind it — without this, GetScore keeps
        // re-entering Returning forever on PendingSurvey.
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
            "Return not ready (mounted={Mounted}, combat={Combat}, jumping={Jumping}, occupied={Occupied}, status={Status}, recast={Recast:F2}, animLock={AnimLock:F2})",
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

        // The idle latch is spent once we leave Returning — either the Return cast, or something
        // (triage / a live FATE goal) pulled us off it and the next idle stretch rolls its own wait.
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
        // Paused = yielded to FATE/CE; Automator must be able to Return / buff / choose.
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
