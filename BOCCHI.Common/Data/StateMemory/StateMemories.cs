using BOCCHI.Common.Data.CriticalEncounters;
using BOCCHI.Common.Data.Fates;
using BOCCHI.Common.Data.Goals;
using BOCCHI.Common.Data.Paths;
using BOCCHI.Common.Data.SupportJobs;
using BOCCHI.Common.Data.Zones;
using BOCCHI.Common.Services.Paths;
using System.Numerics;

namespace BOCCHI.Common.Data.StateMemory;

public sealed class ApplyingBuffsMemory;

public sealed class ManualBuffRunMemory;

/// <summary>Inquiring Mind already ran this buff cycle — do not cast it again.</summary>
public sealed class InquiringMindAttemptedMemory;

public sealed class CastingTreasureSightMemory;

/// <summary>
///     Post-activity Treasure Sight latch for Illegal Mode auto hunts.
/// </summary>
public sealed class AutomaticTreasureSurveyMemory
{
    /// <summary>Cast Sight when idle at base camp.</summary>
    public bool PendingSurvey { get; set; }

    /// <summary>Waiting for WideText after a Sight cast.</summary>
    public bool WaitingForSurveyResult { get; set; }

    /// <summary>Accept surveys with Tracker.SurveyRevision &gt; this value.</summary>
    public int MinAcceptedRevision { get; set; }

    public DateTime SurveyWaitDeadlineUtc { get; set; }
}

public sealed class WaitingForCriticalEncounterMemory(CriticalEncounterId encounterId)
{
    public CriticalEncounterId EncounterId { get; } = encounterId;

    public DateTimeOffset? BattleStartedAtUtc { get; private set; }

    public bool IsFor(CriticalEncounterId id) => EncounterId == id;

    public void MarkBattleStarted()
    {
        BattleStartedAtUtc ??= DateTimeOffset.UtcNow;
    }
}

/// <summary>
///     In FATE/CE combat — block travel replan until the activity goal is dropped.
///     Avoids edge stutter when FATE sync flickers and Pathfinding fights BOCCHI AI.
/// </summary>
public sealed class SuspendTravelForActivityMemory;

/// <summary>
///     Arrived at predicted pot stand-off; hold until the FATE spawns (#112).
/// </summary>
public sealed class WaitingForPotFateMemory;

/// <summary>
///     User / soft-cancel stopped navigation. Blocks auto-replan until Illegal Mode is toggled.
/// </summary>
public sealed class NavigationInterruptedMemory;

/// <summary>Random idle at camp before the outbound teleport to a FATE/CE (#138).</summary>
public sealed class BaseTeleportDelayMemory(TimeSpan delay)
{
    private readonly DateTime startedUtc = DateTime.UtcNow;

    public TimeSpan Delay { get; } = delay;

    public bool IsReady() => DateTime.UtcNow - startedUtc >= Delay;

    public TimeSpan Remaining()
    {
        TimeSpan left = Delay - (DateTime.UtcNow - startedUtc);
        return left > TimeSpan.Zero ? left : TimeSpan.Zero;
    }
}

/// <summary>
///     One initial combat approach per FATE/CE. Re-arms when the activity id changes.
/// </summary>
public sealed class InitialCombatApproachMemory<TActivityId>
    where TActivityId : struct
{
    private TActivityId? activityId;

    public bool IsPending { get; private set; }

    public void Track(TActivityId? currentActivityId)
    {
        if (Nullable.Equals(activityId, currentActivityId))
        {
            return;
        }

        activityId = currentActivityId;
        IsPending = currentActivityId.HasValue;
    }

    public void Complete()
    {
        IsPending = false;
    }
}

public sealed class GoalMemory(IGoal goal)
{
    public IGoal Goal
    {
        get => goal;
    }
}

public sealed class IdleStateMemory(TimeSpan returnAfter)
{
    public readonly DateTimeOffset Entered = DateTimeOffset.UtcNow;

    /// <summary>Rolled wait (2..max) before opportunistic Return while idle.</summary>
    public readonly TimeSpan ReturnAfter = returnAfter;

    public int ApproachCandidateIndex { get; set; }

    public TimeSpan GetIdleTime() => DateTimeOffset.UtcNow - Entered;

    public bool IsReadyToReturn() => GetIdleTime() >= ReturnAfter;
}

public sealed class ReturningStateMemory(TimeSpan castDelay)
{
    public readonly DateTimeOffset QueuedAt = DateTimeOffset.UtcNow;

    /// <summary>Rolled wait before casting Return (path handoff after FATE/CE). Zero when already waited while idle.</summary>
    public readonly TimeSpan CastDelay = castDelay;

    public TimeSpan GetTimeQueued() => DateTimeOffset.UtcNow - QueuedAt;

    public bool IsReadyToCast() => GetTimeQueued() >= CastDelay;
}

public class BuffSupportJobMemory(SupportJobId job)
{
    public readonly SupportJobId Job = job;
}

public class TreasureSightSupportJobMemory(SupportJobId job)
{
    public readonly SupportJobId Job = job;
}

public enum PotChestFarmMode
{
    /// <summary>Magical Elixir + compass hints (South Horn authored groups / North Horn binned spots).</summary>
    Smart,

    /// <summary>Visit authored positions (missing buff/elixir/hints, or rerolls).</summary>
    Blind,
}

public enum PotChestFarmPhase
{
    WaitingForBuff,
    ApproachCenter,
    ElixirAtCenter,
    SearchingCandidates,
    OpeningReveal,
    BlindSweep,
}

public sealed class PotChestFarmMemory
{
    private PotChestFarmMemory(
        FateId fateId,
        PotChestFarmMode mode,
        Vector3 fateCenter,
        IEnumerable<Vector3> blindPositions)
    {
        FateId = fateId;
        Mode = mode;
        FateCenter = fateCenter;
        Chests = new Queue<Vector3>(blindPositions);
        BlindTotalChests = Chests.Count;
        Phase = mode == PotChestFarmMode.Smart
            ? PotChestFarmPhase.WaitingForBuff
            : PotChestFarmPhase.BlindSweep;
        PhaseStartedUtc = DateTimeOffset.UtcNow;
    }

    public static PotChestFarmMemory CreateSmart(FateId fateId, Vector3 fateCenter) =>
        new(fateId, PotChestFarmMode.Smart, fateCenter, []);

    public static PotChestFarmMemory CreateBlind(FateId fateId, IEnumerable<Vector3> chestPositions) =>
        new(fateId, PotChestFarmMode.Blind, Vector3.Zero, chestPositions);

    public FateId FateId { get; }

    public PotChestFarmMode Mode { get; private set; }

    public PotChestFarmPhase Phase { get; set; }

    public Vector3 FateCenter { get; }

    public readonly Queue<Vector3> Chests;

    public int BlindTotalChests { get; private set; }

    public readonly Queue<PotTreasureCandidate> Candidates = new();

    public int CandidateTotal { get; set; }

    public string? ActiveGroupKey { get; set; }

    public DateTimeOffset PhaseStartedUtc { get; set; }

    public DateTimeOffset SettledAtUtc { get; set; } = DateTimeOffset.MinValue;

    public int ElixirAttempts { get; set; }

    public int HintRevisionBaseline { get; set; }

    public int RefineSteps { get; set; }

    public Vector3? RefineTarget { get; set; }

    /// <summary>Finish farm after the current open chain completes (smart reveal).</summary>
    public bool FinishAfterOpen { get; set; }

    /// <summary>When we started waiting for the current (peek) blind chest to spawn.</summary>
    public DateTimeOffset WaitingForSpawnSince { get; set; } = DateTimeOffset.MinValue;

    public int RemainingChests => Mode == PotChestFarmMode.Smart
        ? (Phase is PotChestFarmPhase.SearchingCandidates or PotChestFarmPhase.OpeningReveal
            ? Candidates.Count
            : Math.Max(CandidateTotal, 1))
        : Chests.Count;

    public int TotalChests => Mode == PotChestFarmMode.Smart
        ? Math.Max(CandidateTotal, 1)
        : BlindTotalChests;

    public void BeginBlindFallback(IEnumerable<Vector3> positions)
    {
        Mode = PotChestFarmMode.Blind;
        Phase = PotChestFarmPhase.BlindSweep;
        Chests.Clear();
        foreach (Vector3 p in positions)
        {
            Chests.Enqueue(p);
        }

        BlindTotalChests = Chests.Count;
        Candidates.Clear();
        CandidateTotal = 0;
        ActiveGroupKey = null;
        ElixirAttempts = 0;
        RefineSteps = 0;
        RefineTarget = null;
        WaitingForSpawnSince = DateTimeOffset.MinValue;
        PhaseStartedUtc = DateTimeOffset.UtcNow;
    }

    public void BeginCandidateSearch(string groupKey, IEnumerable<PotTreasureCandidate> ordered)
    {
        ActiveGroupKey = groupKey;
        Candidates.Clear();
        foreach (PotTreasureCandidate c in ordered)
        {
            Candidates.Enqueue(c);
        }

        CandidateTotal = Candidates.Count;
        ElixirAttempts = 0;
        RefineSteps = 0;
        RefineTarget = null;
        SettledAtUtc = DateTimeOffset.MinValue;
        Phase = PotChestFarmPhase.SearchingCandidates;
        PhaseStartedUtc = DateTimeOffset.UtcNow;
    }
}

public sealed class GoalPathStepMemory(IGoal goal, IPathCalculator calculator, bool pauseWhenPlanCompletes = false)
{
    private Task<Queue<IPathStep>>? pathStepTask = calculator.Calculate(goal);

    private bool emptyPlan;

    /// <summary>When true, finishing the plan (or an empty teleport-only plan) pauses nav for manual travel (#139).</summary>
    public bool PauseWhenPlanCompletes { get; } = pauseWhenPlanCompletes;

    public Queue<IPathStep> PathSteps { get; private set; } = [];

    public bool IsCalculating => pathStepTask is { IsCompleted: false };

    /// <summary>Calc finished with zero steps (already at destination, or walks-only plan stripped).</summary>
    public bool IsEmptyPlan => emptyPlan && pathStepTask == null;

    public bool IsValid => pathStepTask != null || PathSteps.Count != 0 || emptyPlan;

    public void Update()
    {
        if (pathStepTask == null)
        {
            return;
        }

        if (!pathStepTask.IsCompleted)
        {
            return;
        }

        if (pathStepTask.IsCompletedSuccessfully)
        {
            PathSteps = pathStepTask.Result;
            emptyPlan = PathSteps.Count == 0;
        }

        pathStepTask = null;
    }

    public IPathStep? GetNextPathStep() => PathSteps.Count > 0 && PathSteps.TryPeek(out IPathStep? step) ? step : null;

    public void DequeuePathStep()
    {
        if (PathSteps.Any())
        {
            PathSteps.Dequeue();
        }
    }
}
