using BOCCHI.Common.Data.StateMemory;
using BOCCHI.Common.Data.SupportJobs;
using BOCCHI.Common.Services;

namespace BOCCHI.Automator.Services;

internal static class IllegalModeActivityWork
{
    public static bool HasPrimaryActivity(IAutomatorMemory memory) =>
        memory.TryRemember<GoalMemory>(out GoalMemory _)
        || memory.TryRemember<WaitingForCriticalEncounterMemory>(out WaitingForCriticalEncounterMemory _)
        || memory.TryRemember<WaitingForPotFateMemory>(out WaitingForPotFateMemory _)
        || memory.TryRemember<GoalPathStepMemory>(out GoalPathStepMemory _)
        || memory.TryRemember<SuspendTravelForActivityMemory>(out SuspendTravelForActivityMemory _)
        || memory.TryRemember<CommittedCriticalEncounterMemory>(out CommittedCriticalEncounterMemory _)
        || memory.TryRemember<CommittedFateMemory>(out CommittedFateMemory _);

    public static bool HasFillerBlockingActivity(IAutomatorMemory memory) =>
        HasPrimaryActivity(memory)
        || memory.TryRemember<PotChestFarmMemory>(out PotChestFarmMemory _)
        || memory.TryRemember<PendingPotChestFarmMemory>(out PendingPotChestFarmMemory _)
        || memory.TryRemember<ApplyingBuffsMemory>(out ApplyingBuffsMemory _)
        || memory.TryRemember<CastingTreasureSightMemory>(out CastingTreasureSightMemory _)
        || TriageSession.IsActive(memory);

    public static bool HasPendingJobRestore(IAutomatorMemory memory) =>
        TryGetPendingJobRestore(memory, out _);

    public static bool TryGetPendingJobRestore(IAutomatorMemory memory, out SupportJobId jobId)
    {
        if (memory.TryRemember<BuffSupportJobMemory>(out BuffSupportJobMemory buff))
        {
            jobId = buff.Job;
            return true;
        }

        if (memory.TryRemember<TreasureSightSupportJobMemory>(out TreasureSightSupportJobMemory sight))
        {
            jobId = sight.Job;
            return true;
        }

        if (memory.TryRemember<TriageSupportJobMemory>(out TriageSupportJobMemory triage))
        {
            jobId = triage.Job;
            return true;
        }

        jobId = default;
        return false;
    }

    public static void ForgetJobRestoreMemories(IAutomatorMemory memory)
    {
        memory.Forget<BuffSupportJobMemory>();
        memory.Forget<TreasureSightSupportJobMemory>();
        memory.Forget<TriageSupportJobMemory>();
    }

    public static bool TryClearCompletedJobRestore(IAutomatorMemory memory, ISupportJobFactory jobs)
    {
        if (!HasPendingJobRestore(memory))
        {
            return true;
        }

        if (!jobs.TryGetCurrent(out SupportJob current))
        {
            return false;
        }

        ForgetIfMatchesCurrent<BuffSupportJobMemory>(memory, current.Id, m => m.Job);
        ForgetIfMatchesCurrent<TreasureSightSupportJobMemory>(memory, current.Id, m => m.Job);
        ForgetIfMatchesCurrent<TriageSupportJobMemory>(memory, current.Id, m => m.Job);

        return !HasPendingJobRestore(memory);
    }

    public static bool TryRememberPreBuffJob(IAutomatorMemory memory, ISupportJobFactory jobs)
    {
        if (memory.TryRemember<BuffSupportJobMemory>(out _))
        {
            return false;
        }

        if (!jobs.TryGetCurrent(out SupportJob current))
        {
            return false;
        }

        return memory.TryAdd(new BuffSupportJobMemory(current.Id));
    }

    public static void ForgetTravelLatches(IAutomatorMemory memory, bool includePotChests = false)
    {
        memory.Forget<GoalPathStepMemory>();
        memory.Forget<WaitingForCriticalEncounterMemory>();
        memory.Forget<WaitingForPotFateMemory>();
        memory.Forget<SuspendTravelForActivityMemory>();
        memory.Forget<CommittedCriticalEncounterMemory>();
        memory.Forget<CommittedFateMemory>();
        if (includePotChests)
        {
            memory.Forget<PotChestFarmMemory>();
        }
    }

    public static RouteUnreachableGoalMemory? TakeActiveUnreachable(IAutomatorMemory memory)
    {
        if (!memory.TryRemember<RouteUnreachableGoalMemory>(out RouteUnreachableGoalMemory skip))
        {
            return null;
        }

        if (skip.IsExpired)
        {
            memory.Forget<RouteUnreachableGoalMemory>();
            return null;
        }

        return skip;
    }

    private static void ForgetIfMatchesCurrent<T>(
        IAutomatorMemory memory,
        SupportJobId current,
        Func<T, SupportJobId> job) where T : class
    {
        if (memory.TryRemember(out T saved) && job(saved) == current)
        {
            memory.Forget<T>();
        }
    }
}

internal static class TriageSession
{
    public static bool IsActive(IAutomatorMemory memory) =>
        memory.TryRemember<PendingTriageMemory>(out PendingTriageMemory _)
        || memory.TryRemember<TriagingMemory>(out TriagingMemory _);

    public static void Clear(IAutomatorMemory memory)
    {
        memory.Forget<PendingTriageMemory>();
        memory.Forget<TriagingMemory>();
    }
}
