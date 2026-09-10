using BOCCHI.Automator.Data;
using BOCCHI.Common.Config;
using BOCCHI.Common.Data.Fates;
using BOCCHI.Common.Data.Goals;
using BOCCHI.Common.Data.StateMemory;
using BOCCHI.Common.Data.SupportJobs;
using BOCCHI.Common.Data.Zones;
using BOCCHI.Common.Services;
using BOCCHI.Treasure.Hunt;
using BOCCHI.Treasure.Services;
using ECommons.Throttlers;
using Ocelot.Lifecycle;
using Ocelot.Services.Logger;

namespace BOCCHI.Automator.Services;

/// <summary>Illegal Mode post-activity treasure filler.</summary>
public class IllegalModeTreasureFillerService
(
    IAutomator automator,
    IAutomatorContext context,
    IAutomatorMemory memory,
    ITreasureHunter hunter,
    ITreasureTracker tracker,
    ISupportJobFactory supportJobs,
    IZoneProvider zones,
    IPotCycleTracker potCycle,
    IFateRepository fates,
    IIllegalModeStartableActivityProbe startableActivities,
    AutomatorConfig automatorConfig,
    FatesConfig fatesConfig,
    PotsConfig potsConfig,
    TreasureConfig treasureConfig,
    ILogger<IllegalModeTreasureFillerService> logger
) : IOnUpdate
{
    // TriageLatchService Order 10 runs before this (Order 0) on the update pass.
    public int Order => 0;

    private bool hadPrimaryActivity;

    private bool hadFillerHunt;

    private bool loggedSightUnavailable;

    /// <summary>
    ///     Last primary Goal that was a pot FATE. Survives Goal forget so post-activity hunt
    ///     can defer to Magical Elixir / pot chests even when Automator missed the same-frame latch
    ///     (e.g. after manually stopping the filler hunt).
    /// </summary>
    private FateId? potFatePrimary;

    private bool HasTreasureSight => SupportJobTreasureSight.CanCast(supportJobs);

    /// <summary>Map hunt always yields to FATEs; Sight hunt only when the FATE option is on.</summary>
    private bool YieldsHuntToFate =>
        !HasTreasureSight || automatorConfig.PauseAutoTreasureHuntForFate;

    /// <summary>Map hunt always yields to CEs; Sight hunt only when the CE option is on.</summary>
    private bool YieldsHuntToCriticalEncounter =>
        !HasTreasureSight || automatorConfig.PauseAutoTreasureHuntForCriticalEncounter;

    /// <summary>True when the hunt can pause for at least one activity type.</summary>
    private bool YieldsHuntToAnyActivity => YieldsHuntToFate || YieldsHuntToCriticalEncounter;

    public void Update()
    {
        if ((!context.IsIllegalMode && !context.IsCompletionist) || context.IsPotsAndTreasure)
        {
            ResetSession();
            return;
        }

        if (automator.SuspendedForShopping)
        {
            return;
        }

        if (!automatorConfig.EnableAutomaticTreasureHuntDuringIllegalMode)
        {
            ResetSession();
            return;
        }

        if (!zones.GetZone().IsOccultCrescentZone())
        {
            ResetSession();
            return;
        }

        EnsureSurveyMemory(out AutomaticTreasureSurveyMemory survey);
        ClearSurveyLatchIfSightUnavailable(survey);

        bool fillerBusy = IllegalModeActivityWork.HasFillerBlockingActivity(memory);
        bool primaryActivityNow = IllegalModeActivityWork.HasPrimaryActivity(memory);
        if (primaryActivityNow)
        {
            RememberPotPrimaryIfAny();
        }

        if (hadPrimaryActivity && !primaryActivityNow)
        {
            OnActivityCompleted(survey);
        }

        hadPrimaryActivity = primaryActivityNow;

        if (hunter.ManagedByIllegalModeFiller && hunter.Running)
        {
            hadFillerHunt = true;
            UpdateRunningFillerHunt(fillerBusy);
            return;
        }

        if (hadFillerHunt && (!hunter.Running || !hunter.ManagedByIllegalModeFiller))
        {
            OnFillerHuntEnded(survey);
            hadFillerHunt = false;
        }

        if (fillerBusy || ShouldDeferToPotChestFarm())
        {
            PauseFillerHuntForActivity();
            return;
        }

        if (survey.WaitingForSurveyResult)
        {
            TryApplySurveyResult(survey);
            return;
        }

        if (survey.PendingSurvey)
        {
            // CastingTreasureSightHandler casts at camp; ReturningHandler gets us there.
            return;
        }

        if (survey.PendingMapHunt)
        {
            TryStartPendingMapHunt(survey);
            return;
        }

        if (ShouldStartHunt(survey))
        {
            EnterHuntPhase(fromSurvey: true);
        }
    }

    /// <summary>
    ///     When yielding is enabled (map hunt, or Sight hunt with FATE/CE pause options), pause for
    ///     a matching active or startable activity so Illegal Mode can take it, then resume afterward.
    ///     Pot leave-early (#204) yields when "Pause auto treasure hunt for pots" is on — even if
    ///     Pause-for-FATE is off — so the hunt does not ignore predicted pot spawns.
    /// </summary>
    private void UpdateRunningFillerHunt(bool activityNow)
    {
        // Pot FATE ending starts FarmingPotChests — leave-early yield ends then, but the hunt
        // must stay paused for the whole farm. Otherwise ResumeNearPlayer fights Automator's
        // SuspendedForTreasure + PotChestFarmMemory latch every tick (idle elixir / no pathing).
        if (ShouldDeferToPotChestFarm())
        {
            PauseHuntForYield("pot chest farm");
            return;
        }

        if (ShouldYieldHuntForImminentPot())
        {
            PauseHuntForYield("pot");
            if (EzThrottler.Throttle("IllegalModeMapHuntYieldPot", 5000))
            {
                logger.Info(
                    "Illegal Mode: pausing treasure hunt — pot within {Minutes}m leave-early window",
                    potsConfig.PotSpawnLeadMinutes);
            }

            return;
        }

        if (!YieldsHuntToAnyActivity)
        {
            // Sight hunt with both pause options off — still resume after a pot leave-early pause.
            if (hunter.Paused)
            {
                EnterHuntPhase(fromSurvey: false);
            }

            return;
        }

        if (activityNow)
        {
            if (ShouldPauseForCurrentActivity())
            {
                PauseHuntForYield("activity");
                return;
            }

            // Triage / buffs / camp Sight block the Automator SM — still check startable CE/FATE.
            if (TryPauseForStartableYield())
            {
                return;
            }

            if (hunter.Paused)
            {
                // Triage / buffs / Sight after a yielded FATE/CE — stay paused and unsuspended.
                automator.SetSuspendedForTreasure(false);
            }

            return;
        }

        if (TryPauseForStartableYield())
        {
            return;
        }

        bool startableMatch = HasStartableYieldTarget(out string kind);
        if (hunter.Paused && !startableMatch)
        {
            // Activity cancelled / nothing matching left — keep filling the map.
            EnterHuntPhase(fromSurvey: false);
        }
    }

    /// <summary>
    ///     True when leave-early / a live pot should pause auto treasure hunt.
    ///     Live pots come from the FATE list, not only the shared timer.
    /// </summary>
    private bool ShouldYieldHuntForImminentPot()
    {
        if (!automatorConfig.PauseAutoTreasureHuntForPots)
        {
            return false;
        }

        if (!FarmsPotChests && !automatorConfig.ShouldPrepositionToPots)
        {
            return false;
        }

        IZone zone = zones.GetZone();
        foreach (Fate fate in fates.Snapshot())
        {
            if (!zone.IsPotFate(fate.Id.Value)
                || !fatesConfig.IsFateEnabledForIllegalMode(
                    fate.Id.Value,
                    isPotFate: true,
                    automatorConfig.PreferPotFates)
                || potsConfig.ShouldSkipLivePot(fate.TimeRemainingSeconds))
            {
                continue;
            }

            return true;
        }

        PotCycleSnapshot cycle = potCycle.Snapshot;
        uint potId = cycle.CurrentActivePotFateId != 0
            ? (uint)cycle.CurrentActivePotFateId
            : (uint)cycle.PredictedNextPotFateId;

        if (potId == 0
            || !fatesConfig.IsFateEnabledForIllegalMode(
                potId,
                isPotFate: true,
                automatorConfig.PreferPotFates))
        {
            return false;
        }

        if (cycle.CurrentActivePotFateId != 0)
        {
            return true;
        }

        return PotFallbackWindow.ShouldPreposition(
            cycle,
            DateTimeOffset.UtcNow,
            potsConfig.PotSpawnLeadMinutes,
            potFarmingEnabled: true);
    }

    private bool TryPauseForStartableYield()
    {
        if (hunter.Paused || !HasStartableYieldTarget(out string kind))
        {
            return false;
        }

        PauseHuntForYield(kind);
        if (EzThrottler.Throttle("IllegalModeMapHuntYield", 5000))
        {
            logger.Info("Illegal Mode: pausing treasure hunt — {Kind} available", kind);
        }

        return true;
    }

    private void PauseHuntForYield(string reason)
    {
        if (!hunter.Paused)
        {
            hunter.Pause();
            logger.Debug("Illegal Mode: paused treasure hunt for {Reason}", reason);
        }

        automator.SetSuspendedForTreasure(false);
    }

    private bool HasStartableYieldTarget(out string kind)
    {
        if (YieldsHuntToCriticalEncounter && startableActivities.HasStartableCriticalEncounter())
        {
            kind = "CE";
            return true;
        }

        if (YieldsHuntToFate && startableActivities.HasStartableFate())
        {
            kind = "FATE";
            return true;
        }

        kind = "";
        return false;
    }

    /// <summary>
    ///     Map hunt pauses for any filler-blocking work. Sight hunt only pauses for FATE/CE that
    ///     match the enabled options (plus follow-on triage/buffs while already paused).
    /// </summary>
    private bool ShouldPauseForCurrentActivity()
    {
        if (!HasTreasureSight)
        {
            return true;
        }

        if (YieldsHuntToCriticalEncounter && IsCriticalEncounterActivity())
        {
            return true;
        }

        if (YieldsHuntToFate && IsFateActivity())
        {
            return true;
        }

        return false;
    }

    private bool IsCriticalEncounterActivity()
    {
        if (memory.TryRemember<WaitingForCriticalEncounterMemory>(out WaitingForCriticalEncounterMemory _)
            || memory.TryRemember<CommittedCriticalEncounterMemory>(out CommittedCriticalEncounterMemory _))
        {
            return true;
        }

        return memory.TryRemember<GoalMemory>(out GoalMemory goal)
               && goal.Goal.GoalType is CriticalEncounterGoal;
    }

    private bool IsFateActivity()
    {
        if (memory.TryRemember<WaitingForPotFateMemory>(out WaitingForPotFateMemory _)
            || memory.TryRemember<PotChestFarmMemory>(out PotChestFarmMemory _)
            || memory.TryRemember<PendingPotChestFarmMemory>(out PendingPotChestFarmMemory _))
        {
            return true;
        }

        return memory.TryRemember<GoalMemory>(out GoalMemory goal)
               && goal.Goal.GoalType is FateGoal;
    }

    private void EnsureSurveyMemory(out AutomaticTreasureSurveyMemory survey)
    {
        if (memory.TryRemember(out survey))
        {
            return;
        }

        survey = new AutomaticTreasureSurveyMemory();
        memory.TryAdd(survey);
    }

    private void OnActivityCompleted(AutomaticTreasureSurveyMemory survey)
    {
        // TriageLatchService owns raise latch; wait until it finishes before Sight / map hunt.
        if (TriageSession.IsActive(memory))
        {
            return;
        }

        // Pot chests + Magical Elixir beat Sight survey / map resume — even if a survey was
        // already latched, or Automator missed CreateSmart this frame after a manual hunt stop.
        if (TryDeferForPotChestFarm(survey))
        {
            return;
        }

        if (survey.IsBusy)
        {
            return;
        }

        // Same map-hunt session was paused for this FATE/CE — continue remaining pads.
        if (hunter.ManagedByIllegalModeFiller && hunter.Running && hunter.Paused)
        {
            logger.Info("Illegal Mode: resuming map treasure hunt after FATE/CE");
            EnterHuntPhase(fromSurvey: false);
            return;
        }

        LatchPostActivityHunt(survey, "activity completed");
    }

    private void RememberPotPrimaryIfAny()
    {
        if (!FarmsPotChests)
        {
            return;
        }

        if (memory.TryRemember<GoalMemory>(out GoalMemory goal)
            && goal.Goal.GoalType is FateGoal fate
            && zones.GetZone().IsPotFate(fate.id.Value))
        {
            potFatePrimary = fate.id;
        }
    }

    private bool FarmsPotChests =>
        automatorConfig.ShouldFarmPotChests || context.IsPotsAndTreasure;

    /// <summary>
    ///     Prefer pot chest / elixir wait over post-activity Sight or map hunt.
    /// </summary>
    private bool TryDeferForPotChestFarm(AutomaticTreasureSurveyMemory survey)
    {
        if (!FarmsPotChests)
        {
            potFatePrimary = null;
            return false;
        }

        FateId? expectedPot = potFatePrimary;
        potFatePrimary = null;

        if (!ShouldDeferToPotChestFarm())
        {
            if (expectedPot is not { } potId)
            {
                return false;
            }

            // Automator.Update runs first and normally CreateSmarts; if it did not (stop / suspend
            // race), arm Pending so the next Automator tick starts WaitingForBuff → elixir.
            if (!memory.TryRemember<PendingPotChestFarmMemory>(out PendingPotChestFarmMemory _)
                && !memory.TryRemember<PotChestFarmMemory>(out PotChestFarmMemory _))
            {
                memory.TryAdd(new PendingPotChestFarmMemory(potId));
                logger.Info(
                    "Illegal Mode: deferring treasure hunt for pot chests (fate {FateId})",
                    potId.Value);
            }
        }

        if (!ShouldDeferToPotChestFarm())
        {
            return false;
        }

        ClearPostActivityHuntLatch(survey);
        return true;
    }

    private bool ShouldDeferToPotChestFarm()
    {
        if (!FarmsPotChests)
        {
            return false;
        }

        return memory.TryRemember<PotChestFarmMemory>(out PotChestFarmMemory _)
               || memory.TryRemember<PendingPotChestFarmMemory>(out PendingPotChestFarmMemory _);
    }

    private static void ClearPostActivityHuntLatch(AutomaticTreasureSurveyMemory survey)
    {
        survey.PendingSurvey = false;
        survey.WaitingForSurveyResult = false;
        survey.PendingMapHunt = false;
        survey.SurveyWaitDeadlineUtc = DateTime.MinValue;
    }

    private void LatchPostActivityHunt(AutomaticTreasureSurveyMemory survey, string reason)
    {
        if (!HasTreasureSight)
        {
            survey.PendingSurvey = false;
            survey.WaitingForSurveyResult = false;
            survey.SurveyWaitDeadlineUtc = DateTime.MinValue;
            survey.PendingMapHunt = true;
            // Hunt owns travel/Return — drop any Automator Return already queued after the FATE/CE.
            memory.Forget<ReturningStateMemory>();
            LogSightUnavailableOnce();
            logger.Debug("Illegal Mode: latched map treasure hunt without Treasure Sight ({Reason})", reason);
            return;
        }

        survey.PendingMapHunt = false;
        survey.PendingSurvey = true;
        survey.WaitingForSurveyResult = false;
        survey.MinAcceptedRevision = tracker.SurveyRevision;
        survey.SurveyWaitDeadlineUtc = DateTime.MinValue;
        logger.Debug("Illegal Mode: latched Treasure Sight survey ({Reason})", reason);
    }

    private void ClearSurveyLatchIfSightUnavailable(AutomaticTreasureSurveyMemory survey)
    {
        if (HasTreasureSight)
        {
            loggedSightUnavailable = false;
            return;
        }

        if (survey.PendingSurvey || survey.WaitingForSurveyResult)
        {
            survey.PendingSurvey = false;
            survey.WaitingForSurveyResult = false;
            survey.SurveyWaitDeadlineUtc = DateTime.MinValue;
            survey.PendingMapHunt = true;
            memory.Forget<ReturningStateMemory>();
            LogSightUnavailableOnce();
            logger.Debug("Illegal Mode: Treasure Sight became unavailable — falling back to map hunt");
        }
    }

    private void LogSightUnavailableOnce()
    {
        if (loggedSightUnavailable)
        {
            return;
        }

        loggedSightUnavailable = true;
        logger.Info(
            "Illegal Mode: Treasure Sight unavailable (Freelancer below level {Level}) — using built-in coffer map (yields to FATE/CE)",
            SupportJobTreasureSight.RequiredFreelancerLevel);
    }

    private void TryStartPendingMapHunt(AutomaticTreasureSurveyMemory survey)
    {
        if (!hunter.IsVnavAvailable || TriageSession.IsActive(memory))
        {
            return;
        }

        // Prefer a live FATE/CE before burning a full map pass.
        if (startableActivities.HasStartableFateOrCriticalEncounter())
        {
            return;
        }

        if (ShouldYieldHuntForImminentPot())
        {
            return;
        }

        if (automator.CurrentState is not (AutomatorState.Idle or null))
        {
            return;
        }

        // No Sight → no live fill counts. Always run the built-in map; thresholds only apply after a survey.
        survey.PendingMapHunt = false;
        EnterHuntPhase(fromSurvey: false);
    }

    private void TryApplySurveyResult(AutomaticTreasureSurveyMemory survey)
    {
        if (tracker.SurveyRevision > survey.MinAcceptedRevision && tracker.CountInitialised)
        {
            ApplySurveyResult(survey);
            return;
        }

        if (survey.SurveyWaitDeadlineUtc != DateTime.MinValue
            && DateTime.UtcNow >= survey.SurveyWaitDeadlineUtc)
        {
            survey.WaitingForSurveyResult = false;
            survey.PendingSurvey = false;
            logger.Debug("Illegal Mode: Treasure Sight survey timed out — retry after next activity");
        }
    }

    private void ApplySurveyResult(AutomaticTreasureSurveyMemory survey)
    {
        survey.WaitingForSurveyResult = false;
        survey.PendingSurvey = false;

        int silver = tracker.SilverChests;
        int bronze = tracker.BronzeChests;
        if (silver + bronze <= 0)
        {
            logger.Info("Illegal Mode: survey found no coffers — continuing CE/FATE farming");
            return;
        }

        if (!TreasureHuntFillGate.MeetsMinimumFill(tracker, treasureConfig))
        {
            logger.Info(
                "Illegal Mode: survey fill below threshold ({Silver} silver, {Bronze} bronze) — continuing CE/FATE farming",
                silver,
                bronze);
            return;
        }

        if (ShouldYieldHuntForImminentPot())
        {
            survey.PendingMapHunt = true;
            logger.Info(
                "Illegal Mode: survey found {Silver} silver, {Bronze} bronze — deferring hunt until after pot leave-early",
                silver,
                bronze);
            return;
        }

        logger.Info(
            "Illegal Mode: survey found {Silver} silver, {Bronze} bronze — starting hunt",
            silver,
            bronze);
        EnterHuntPhase(fromSurvey: true);
    }

    private void OnFillerHuntEnded(AutomaticTreasureSurveyMemory survey)
    {
        // After a route, wait for the next activity before surveying / hunting again.
        survey.PendingSurvey = false;
        survey.WaitingForSurveyResult = false;
        survey.PendingMapHunt = false;
        survey.MinAcceptedRevision = tracker.SurveyRevision;
        automator.SetSuspendedForTreasure(false);
        logger.Info("Illegal Mode: treasure hunt ended — will fill again after next activity");
    }

    private bool ShouldStartHunt(AutomaticTreasureSurveyMemory survey)
    {
        if (!hunter.IsVnavAvailable || survey.IsBusy)
        {
            return false;
        }

        if (ShouldYieldHuntForImminentPot())
        {
            return false;
        }

        if (!tracker.CountInitialised || tracker.SurveyRevision <= survey.MinAcceptedRevision)
        {
            return false;
        }

        if (tracker.SilverChests + tracker.BronzeChests <= 0)
        {
            return false;
        }

        if (!TreasureHuntFillGate.MeetsMinimumFill(tracker, treasureConfig))
        {
            return false;
        }

        return automator.CurrentState is AutomatorState.Idle or null;
    }

    private void EnterHuntPhase(bool fromSurvey)
    {
        if (ShouldDeferToPotChestFarm())
        {
            PauseHuntForYield("pot chest farm");
            return;
        }

        // Map hunts keep Automator awake so a spawned FATE/CE can interrupt.
        // Sight hunts stay suspended until a matching pause option fires (then unsuspend).
        if (!HasTreasureSight)
        {
            automator.SetSuspendedForTreasure(false);
            memory.Forget<ReturningStateMemory>();
        }
        else
        {
            automator.SetSuspendedForTreasure(true);
        }

        if (!hunter.IsVnavReady)
        {
            if (!fromSurvey)
            {
                // Keep retrying the map hunt once navmesh is ready.
                if (memory.TryRemember(out AutomaticTreasureSurveyMemory survey))
                {
                    survey.PendingMapHunt = true;
                }
            }

            return;
        }

        if (!hunter.Running)
        {
            hunter.ManagedByIllegalModeFiller = true;
            hunter.StartManaged();
            hadFillerHunt = true;
            if (fromSurvey && tracker.CountInitialised)
            {
                logger.Info(
                    "Illegal Mode: started automatic treasure hunt (survey {Silver} silver, {Bronze} bronze)",
                    tracker.SilverChests,
                    tracker.BronzeChests);
            }
            else
            {
                logger.Info("Illegal Mode: started automatic treasure hunt from built-in map (no Treasure Sight)");
            }

            return;
        }

        if (hunter.Paused)
        {
            // After a distant FATE/CE/pot, continue from nearby remaining pads instead of walking
            // back to where the route was paused (includes pot leave-early with Sight pause-FATE off).
            hunter.ResumeNearPlayer();
            hadFillerHunt = true;
            logger.Debug("Illegal Mode: resumed automatic treasure hunt");
        }
    }

    private void PauseFillerHuntForActivity()
    {
        automator.SetSuspendedForTreasure(false);

        if (!hunter.ManagedByIllegalModeFiller)
        {
            return;
        }

        if (hunter.Running && !hunter.Paused)
        {
            hunter.Pause();
            logger.Debug("Illegal Mode: paused treasure hunt for CE/FATE activity");
        }
    }

    private void ResetSession()
    {
        hadPrimaryActivity = false;
        hadFillerHunt = false;
        loggedSightUnavailable = false;
        potFatePrimary = null;
        memory.Forget<AutomaticTreasureSurveyMemory>();

        if (hunter.ManagedByIllegalModeFiller)
        {
            automator.SetSuspendedForTreasure(false);
            hunter.ManagedByIllegalModeFiller = false;
            if (hunter.Running)
            {
                hunter.Toggle();
            }
        }
    }
}
