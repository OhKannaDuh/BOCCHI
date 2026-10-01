using BOCCHI.Automator.Data;
using BOCCHI.Automator.Services;
using BOCCHI.Buff.Data;
using BOCCHI.Buff.Services;
using BOCCHI.Common.Config;
using BOCCHI.Common.Data.StateMemory;
using BOCCHI.Common.Data.SupportJobs;
using BOCCHI.Common.Data.Zones;
using BOCCHI.Common.Services;
using Ocelot.Services.Logger;
using Ocelot.Services.Pathfinding;
using Ocelot.States;
using Ocelot.States.Score;

namespace BOCCHI.Automator.StateMachine.Handlers;

public class ApplyingBuffsHandler
(
    Func<IStateMachine<BuffState>> factory,
    IBuffProvider buffs,
    IZoneProvider zones,
    IAutomatorMemory memory,
    ISupportJobFactory jobs,
    IPathfinder pathfinder,
    BuffConfig config,
    ILogger<ApplyingBuffsHandler> logger
) : ScoreStateHandler<AutomatorState, StatePriority>(AutomatorState.ApplyingBuffs)
{
    private IStateMachine<BuffState>? stateMachine;

    public override StatePriority GetScore()
    {
        if (memory.TryRemember<ApplyingBuffsMemory>(out ApplyingBuffsMemory _))
        {
            return StatePriority.VeryHigh;
        }

        if (IllegalModeActivityWork.HasPendingJobRestore(memory)
            || memory.TryRemember<AutomaticTreasureSurveyMemory>(out AutomaticTreasureSurveyMemory survey)
               && survey.IsBusy)
        {
            return StatePriority.Never;
        }

        if (!config.ShouldAutomateBuffs || !buffs.ShouldRefreshAny())
        {
            return StatePriority.Never;
        }

        IZone zone = zones.GetZone();
        if (!zone.IsOccultCrescentZone())
        {
            return StatePriority.Never;
        }

        if (!zone.GetNearbyKnowledgeCrystals().Any())
        {
            return StatePriority.Never;
        }

        return StatePriority.MediumHigh;
    }

    public override void Enter()
    {
        stateMachine = factory();

        memory.TryAdd<ApplyingBuffsMemory>();
        memory.Forget<InquiringMindAttemptedMemory>();
        if (jobs.TryGetCurrent(out SupportJob starting)
            && starting.Id == SupportJobId.PhantomFreelancer)
        {
            logger.Info(
                buffs.CanUseInquiringMind()
                    ? "Illegal Mode buff: already Freelancer — Inquiring Mind, then stay Freelancer"
                    : "Illegal Mode buff: already Freelancer without Inquiring Mind — each buff on its job, then back to Freelancer");
        }

        if (IllegalModeActivityWork.TryRememberPreBuffJob(memory, jobs))
        {
            if (jobs.TryGetCurrent(out SupportJob latched))
            {
                logger.Debug("Illegal Mode buff: latched restore job {Job}", latched.Id);
            }
        }
        else if (IllegalModeActivityWork.TryGetPendingJobRestore(memory, out SupportJobId existing))
        {
            logger.Debug(
                "Illegal Mode buff: kept existing restore latch {Job} (current {Current})",
                existing,
                jobs.TryGetCurrent(out SupportJob cur) ? cur.Id.ToString() : "?");
        }
    }

    public override void Exit(AutomatorState next)
    {
        base.Exit(next);
        if (next != AutomatorState.ApplyingBuffs)
        {
            ClearBuffLatch();
        }
    }

    public override void Handle()
    {
        if (stateMachine == null)
        {
            return;
        }

        stateMachine.Update();

        if (stateMachine.State == BuffState.NoCrystalsFound)
        {
            logger.Warning("Illegal Mode buff run aborted — no knowledge crystals nearby");
            pathfinder.Stop();
            ClearBuffLatch();
            stateMachine = null;
        }
    }

    public override void Render()
    {
        stateMachine?.Render();
    }

    private void ClearBuffLatch()
    {
        memory.Forget<ApplyingBuffsMemory>();
        memory.Forget<InquiringMindAttemptedMemory>();
    }
}
