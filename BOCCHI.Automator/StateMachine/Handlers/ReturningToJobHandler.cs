using BOCCHI.Automator.Data;
using BOCCHI.Automator.Services;
using BOCCHI.Common.Data.StateMemory;
using BOCCHI.Common.Data.SupportJobs;
using BOCCHI.Common.Services;
using Dalamud.Plugin.Services;
using ECommons.Throttlers;
using Ocelot.Services.Logger;
using Ocelot.States.Score;

namespace BOCCHI.Automator.StateMachine.Handlers;

public class ReturningToJobHandler
(
    IAutomatorMemory memory,
    ISupportJobFactory jobs,
    ISupportJobChanger changer,
    ICondition conditions,
    ILogger<ReturningToJobHandler> logger
) : ScoreStateHandler<AutomatorState, StatePriority>(AutomatorState.ReturningToJob)
{
    private const int RestoreTimeoutSeconds = 20;

    private DateTime restorePendingSince = DateTime.MinValue;

    public override StatePriority GetScore()
    {
        if (memory.TryRemember<CastingTreasureSightMemory>(out CastingTreasureSightMemory _)
            || memory.TryRemember<TriagingMemory>(out TriagingMemory _)
            || memory.TryRemember<ApplyingBuffsMemory>(out ApplyingBuffsMemory _))
        {
            return StatePriority.Never;
        }

        return IllegalModeActivityWork.HasPendingJobRestore(memory)
            ? StatePriority.Critical
            : StatePriority.Never;
    }

    public override void Enter()
    {
        base.Enter();
        restorePendingSince = DateTime.UtcNow;
    }

    public override void Exit(AutomatorState next)
    {
        base.Exit(next);
        restorePendingSince = DateTime.MinValue;
    }

    public override void Handle()
    {
        if (!IllegalModeActivityWork.HasPendingJobRestore(memory))
        {
            return;
        }

        if (!EzThrottler.Throttle("ReturningToJobHandler::Gate", 250))
        {
            return;
        }

        if (TryClearOrTimeoutRestore())
        {
            return;
        }

        if (!IllegalModeActivityWork.TryGetPendingJobRestore(memory, out SupportJobId jobId))
        {
            return;
        }

        jobs.TryGetCurrent(out SupportJob current);
        if (changer.IsBusy())
        {
            LogPending(current?.Id, jobId, "job changer busy");
            return;
        }

        if (PhantomJobChangeGate.IsBlocked(conditions))
        {
            LogPending(current?.Id, jobId, "job swap gated (combat, casting, occupied, …)");
            return;
        }

        logger.Debug(
            "Restoring phantom job {From} → {To}",
            current?.Id.ToString() ?? "?",
            jobId);
        changer.Change(jobId);
    }

    private bool TryClearOrTimeoutRestore()
    {
        if (IllegalModeActivityWork.TryClearCompletedJobRestore(memory, jobs))
        {
            if (EzThrottler.Throttle("ReturningToJobHandler::Cleared", 2000))
            {
                logger.Debug("Job restore complete — cleared saved job latch");
            }

            return true;
        }

        if (restorePendingSince == DateTime.MinValue)
        {
            restorePendingSince = DateTime.UtcNow;
        }

        if (DateTime.UtcNow - restorePendingSince < TimeSpan.FromSeconds(RestoreTimeoutSeconds))
        {
            return false;
        }

        jobs.TryGetCurrent(out SupportJob current);
        IllegalModeActivityWork.TryGetPendingJobRestore(memory, out SupportJobId target);
        logger.Warning(
            "Job restore timed out after {Seconds}s (current={Current}, target={Target}) — clearing latch",
            RestoreTimeoutSeconds,
            current?.Id.ToString() ?? "?",
            target);
        IllegalModeActivityWork.ForgetJobRestoreMemories(memory);
        return true;
    }

    private void LogPending(SupportJobId? current, SupportJobId target, string reason)
    {
        if (!EzThrottler.Throttle("ReturningToJobHandler::Pending", 5000))
        {
            return;
        }

        logger.Debug(
            "Job restore waiting: {Reason} (current={Current}, target={Target})",
            reason,
            current?.ToString() ?? "?",
            target);
    }
}
