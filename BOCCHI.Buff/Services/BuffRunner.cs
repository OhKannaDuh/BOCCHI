using BOCCHI.Buff.Data;
using BOCCHI.Common.Data.StateMemory;
using BOCCHI.Common.Data.SupportJobs;
using BOCCHI.Common.Data.Zones;
using BOCCHI.Common.Services;
using ECommons.Throttlers;
using Ocelot.Lifecycle;
using Ocelot.Services.Logger;
using Ocelot.Services.Pathfinding;
using Ocelot.Services.PlayerState;
using Ocelot.States;

namespace BOCCHI.Buff.Services;

public class BuffRunner
(
    Func<IStateMachine<BuffState>> factory,
    IZoneProvider zones,
    IPlayer player,
    IAutomatorMemory memory,
    ISupportJobFactory jobs,
    ISupportJobChanger changer,
    IPathfinder pathfinder,
    ILogger<BuffRunner> logger
) : IBuffRunner, IOnUpdate
{
    private IStateMachine<BuffState>? stateMachine;

    public bool IsRunning { get; private set; }

    public string? DisabledReason { get; private set; }

    public bool CanStart
    {
        get
        {
            DisabledReason = GetDisabledReason();
            return DisabledReason == null;
        }
    }

    public void Start()
    {
        if (!CanStart)
        {
            logger.Warning("Cannot start buff run: {Reason}", DisabledReason ?? "unknown");
            return;
        }

        Begin(manual: true);
    }

    public void StartWalkIn()
    {
        DisabledReason = GetDisabledReason(requireInCastRange: false);
        if (DisabledReason != null)
        {
            logger.Warning("Cannot start buff run: {Reason}", DisabledReason);
            return;
        }

        Begin(manual: false);
    }

    private void Begin(bool manual)
    {
        stateMachine = factory();
        memory.TryAdd<ApplyingBuffsMemory>();
        if (manual)
        {
            memory.TryAdd<ManualBuffRunMemory>();
        }

        memory.Forget<InquiringMindAttemptedMemory>();

        if (jobs.TryGetCurrent(out SupportJob job))
        {
            memory.TryAdd(new BuffSupportJobMemory(job.Id));
        }

        IsRunning = true;
        logger.Info(manual ? "Manual buff run started" : "Buff run started (walking to crystal)");
    }

    public void Stop()
    {
        if (!IsRunning)
        {
            return;
        }

        pathfinder.Stop();
        memory.Forget<ApplyingBuffsMemory>();
        memory.Forget<ManualBuffRunMemory>();
        memory.Forget<InquiringMindAttemptedMemory>();
        RestoreJobIfNeeded();
        CompleteIfJobRestored();
        logger.Info("Manual buff run stopped");
    }

    public void Update()
    {
        if (!IsRunning)
        {
            return;
        }

        if (stateMachine != null)
        {
            stateMachine.Update();

            if (stateMachine.State == BuffState.NoCrystalsFound)
            {
                logger.Warning("Manual buff run aborted — no knowledge crystals nearby");
                pathfinder.Stop();
                memory.Forget<ApplyingBuffsMemory>();
                memory.Forget<ManualBuffRunMemory>();
                memory.Forget<InquiringMindAttemptedMemory>();
                RestoreJobIfNeeded();
                CompleteIfJobRestored();
                return;
            }
        }

        if (memory.TryRemember<ApplyingBuffsMemory>(out ApplyingBuffsMemory _))
        {
            return;
        }

        stateMachine = null;
        memory.Forget<ManualBuffRunMemory>();
        memory.Forget<InquiringMindAttemptedMemory>();
        RestoreJobIfNeeded();
        CompleteIfJobRestored();
    }

    private string? GetDisabledReason(bool requireInCastRange = true)
    {
        if (IsRunning)
        {
            return "Buffs are already being applied.";
        }

        if (memory.TryRemember<ApplyingBuffsMemory>(out ApplyingBuffsMemory _))
        {
            return "Buffs are already being applied.";
        }

        IZone zone = zones.GetZone();
        if (!zone.IsOccultCrescentZone())
        {
            return "Not in a supported Occult Crescent zone.";
        }

        if (!zone.HasNearbyKnowledgeCrystals())
        {
            return "You must be near a knowledge crystal.";
        }

        if (requireInCastRange && !zone.IsInBuffCastRange(player.Position))
        {
            return "Stand in the buff circle at the knowledge crystal.";
        }

        return null;
    }

    private void RestoreJobIfNeeded()
    {
        if (!memory.TryRemember<BuffSupportJobMemory>(out BuffSupportJobMemory saved))
        {
            return;
        }

        if (jobs.TryGetCurrent(out SupportJob current) && current.Id == saved.Job)
        {
            memory.Forget<BuffSupportJobMemory>();
            return;
        }

        if (!EzThrottler.Throttle("BuffRunner::RestoreJob", 250))
        {
            return;
        }

        if (!changer.IsBusy())
        {
            changer.Change(saved.Job);
        }
    }

    private void CompleteIfJobRestored()
    {
        if (memory.TryRemember<BuffSupportJobMemory>(out BuffSupportJobMemory _))
        {
            return;
        }

        IsRunning = false;
        stateMachine = null;
    }
}
