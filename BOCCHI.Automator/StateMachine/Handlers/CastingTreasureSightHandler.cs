using BOCCHI.Automator.Data;
using BOCCHI.Automator.Services;
using BOCCHI.Common.Config;
using BOCCHI.Common.Data.StateMemory;
using BOCCHI.Common.Data.SupportJobs;
using BOCCHI.Common.Data.Zones;
using BOCCHI.Common.Services;
using BOCCHI.Treasure.Services;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using Ocelot.Actions;
using Ocelot.States.Score;

namespace BOCCHI.Automator.StateMachine.Handlers;

public class CastingTreasureSightHandler
(
    ICondition conditions,
    IZoneProvider zone,
    ISupportJobFactory supportJobs,
    ISupportJobChanger changer,
    IAutomatorMemory memory,
    IAutomator automator,
    ITreasureHunter hunter,
    AutomatorConfig automatorConfig,
    ITreasureTracker tracker
) : ScoreStateHandler<AutomatorState, StatePriority>(AutomatorState.CastingTreasureSight)
{
    private DateTime lastCast = DateTime.MinValue;

    public override StatePriority GetScore()
    {
        if (memory.TryRemember<CastingTreasureSightMemory>(out CastingTreasureSightMemory _))
        {
            return StatePriority.Critical;
        }

        if (memory.TryRemember<ApplyingBuffsMemory>(out ApplyingBuffsMemory _)
            || IllegalModeActivityWork.HasPendingJobRestore(memory))
        {
            return StatePriority.Never;
        }

        if (!SupportJobTreasureSight.CanCast(supportJobs))
        {
            return StatePriority.Never;
        }

        if (!zone.GetZone().IsInBasecamp())
        {
            return StatePriority.Never;
        }

        if (automatorConfig.EnableAutomaticTreasureHuntDuringIllegalMode
            && memory.TryRemember<AutomaticTreasureSurveyMemory>(out AutomaticTreasureSurveyMemory survey)
            && survey.PendingSurvey
            && !survey.WaitingForSurveyResult)
        {
            return StatePriority.Always;
        }

        if (!CanCastIdleCampSight())
        {
            return StatePriority.Never;
        }

        return StatePriority.VeryLow;
    }

    public override void Enter()
    {
        base.Enter();

        if (supportJobs.TryGetCurrent(out SupportJob current)
            && current.Id != SupportJobId.PhantomFreelancer)
        {
            memory.Forget<TreasureSightSupportJobMemory>();
            memory.TryAdd(new TreasureSightSupportJobMemory(current.Id));
        }

        memory.TryAdd<CastingTreasureSightMemory>();
    }

    public override void Handle()
    {
        if (!supportJobs.TryGetCurrent(out SupportJob current))
        {
            return;
        }

        if (DismountAssist.TryDismount(conditions))
        {
            return;
        }

        if (current.Id != SupportJobId.PhantomFreelancer)
        {
            if (!changer.IsBusy() && !PhantomJobChangeGate.IsBlocked(conditions))
            {
                changer.Change(SupportJobId.PhantomFreelancer);
            }

            return;
        }

        if (Actions.PhantomActionII.CanCast())
        {
            if (Actions.PhantomActionII.Cast())
            {
                lastCast = DateTime.Now;
                memory.Forget<CastingTreasureSightMemory>();

                if (!memory.TryRemember<AutomaticTreasureSurveyMemory>(out AutomaticTreasureSurveyMemory survey))
                {
                    survey = new AutomaticTreasureSurveyMemory();
                    memory.TryAdd(survey);
                }

                if (survey.PendingSurvey
                    || automatorConfig.EnableAutomaticTreasureHuntDuringIllegalMode)
                {
                    survey.PendingSurvey = false;
                    survey.WaitingForSurveyResult = true;
                    survey.MinAcceptedRevision = tracker.SurveyRevision;
                    survey.SurveyWaitDeadlineUtc = DateTime.UtcNow + TimeSpan.FromSeconds(8);
                }
            }
        }
    }

    private bool CanCastIdleCampSight()
    {
        bool autoHunt = automatorConfig.EnableAutomaticTreasureHuntDuringIllegalMode;
        if (!autoHunt && !automatorConfig.ShouldCastTreasureSight)
        {
            return false;
        }

        if (GetLastCastDeltaSeconds() < automatorConfig.TreasureSightRecastIntervalSeconds)
        {
            return false;
        }

        if (autoHunt)
        {
            if (automator.SuspendedForTreasure
                || automator.SuspendedForShopping
                || (hunter.ManagedByIllegalModeFiller && hunter.Running))
            {
                return false;
            }

            if (memory.TryRemember<AutomaticTreasureSurveyMemory>(out AutomaticTreasureSurveyMemory survey)
                && (survey.IsBusy || survey.PendingMapHunt))
            {
                return false;
            }

            if (IllegalModeActivityWork.HasFillerBlockingActivity(memory))
            {
                return false;
            }
        }

        return true;
    }

    private int GetLastCastDeltaSeconds()
    {
        if (lastCast == DateTime.MinValue)
        {
            return int.MaxValue;
        }

        return (int)(DateTime.Now - lastCast).TotalSeconds;
    }
}
