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

public sealed class InquiringMindAttemptedMemory;

public sealed class CastingTreasureSightMemory;

public sealed class PendingTriageMemory;

public sealed class TriagingMemory;

public sealed class TriageSupportJobMemory(SupportJobId job)
{
    public readonly SupportJobId Job = job;
}

public sealed class AutomaticTreasureSurveyMemory
{
    public bool PendingSurvey { get; set; }

    public bool WaitingForSurveyResult { get; set; }

    public bool PendingMapHunt { get; set; }

    public bool IsBusy => PendingSurvey || WaitingForSurveyResult;

    public int MinAcceptedRevision { get; set; }

    public DateTime SurveyWaitDeadlineUtc { get; set; }
}

public sealed class WaitingForCriticalEncounterMemory(CriticalEncounterId encounterId)
{
    public CriticalEncounterId EncounterId { get; } = encounterId;

    public bool IsFor(CriticalEncounterId id) => EncounterId == id;
}

public sealed class WaitingForForkedTowerMemory(CriticalEncounterId towerId)
{
    public CriticalEncounterId TowerId { get; } = towerId;

    public bool ReminderPrinted { get; set; }

    public DateTimeOffset? BattleSeenAt { get; set; }

    public bool IsFor(CriticalEncounterId id) => TowerId == id;
}

public sealed class CommittedCriticalEncounterMemory(CriticalEncounterId encounterId)
{
    public CriticalEncounterId EncounterId { get; } = encounterId;

    public bool IsFor(CriticalEncounterId id) => EncounterId == id;
}

public sealed class CommittedFateMemory(FateId fateId)
{
    public FateId FateId { get; } = fateId;

    public bool IsFor(FateId id) => FateId == id;
}

public sealed class SuspendTravelForActivityMemory;

public sealed class WaitingForPotFateMemory;

public sealed class PendingPotChestFarmMemory(FateId fateId)
{
    public FateId FateId { get; } = fateId;
}

public sealed class NavigationInterruptedMemory;

public sealed class RouteUnreachableGoalMemory(IGoal goal, TimeSpan ttl)
{
    public IGoal Goal { get; } = goal;

    private readonly DateTimeOffset until = DateTimeOffset.UtcNow + ttl;

    public bool IsExpired => DateTimeOffset.UtcNow >= until;

    public bool MatchesCriticalEncounter(CriticalEncounterId id) =>
        !IsExpired && Goal.GoalType is CriticalEncounterGoal(var ce) && ce == id;

    public bool MatchesForkedTower(CriticalEncounterId id) =>
        !IsExpired && Goal.GoalType is ForkedTowerGoal(var tower) && tower == id;

    public bool MatchesFate(FateId id) =>
        !IsExpired && Goal.GoalType is FateGoal(var fate) && fate == id;
}

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

    public readonly TimeSpan ReturnAfter = returnAfter;

    public int ApproachCandidateIndex { get; set; }

    public List<Vector3>? WaitCandidates { get; set; }

    public TimeSpan GetIdleTime() => DateTimeOffset.UtcNow - Entered;

    public bool IsReadyToReturn() => GetIdleTime() >= ReturnAfter;
}

public sealed class ReturningStateMemory(TimeSpan castDelay)
{
    public readonly DateTimeOffset QueuedAt = DateTimeOffset.UtcNow;

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
    Smart,

    Blind,
}

public enum PotChestFarmPhase
{
    WaitingForBuff,
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
        IEnumerable<Vector3> blindPositions)
    {
        FateId = fateId;
        Mode = mode;
        Chests = new Queue<Vector3>(blindPositions);
        BlindTotalChests = Chests.Count;
        Phase = mode == PotChestFarmMode.Smart
            ? PotChestFarmPhase.WaitingForBuff
            : PotChestFarmPhase.BlindSweep;
        PhaseStartedUtc = DateTimeOffset.UtcNow;
    }

    public static PotChestFarmMemory CreateSmart(FateId fateId) =>
        new(fateId, PotChestFarmMode.Smart, []);

    public static PotChestFarmMemory CreateBlind(FateId fateId, IEnumerable<Vector3> chestPositions) =>
        new(fateId, PotChestFarmMode.Blind, chestPositions);

    public FateId FateId { get; }

    public PotChestFarmMode Mode { get; private set; }

    public PotChestFarmPhase Phase { get; set; }

    public readonly Queue<Vector3> Chests;

    public int BlindTotalChests { get; private set; }

    public readonly Queue<PotTreasureCandidate> Candidates = new();

    public int CandidateTotal { get; set; }

    public readonly List<PotTreasureCandidate> Pool = [];

    public DateTimeOffset PhaseStartedUtc { get; set; }

    public DateTimeOffset SettledAtUtc { get; set; } = DateTimeOffset.MinValue;

    public int ElixirAttempts { get; set; }

    public int HintRevisionBaseline { get; set; }

    public Vector3? ElixirHintOrigin { get; set; }

    public int HintsApplied { get; set; }

    public DateTimeOffset BuffLostUtc { get; set; } = DateTimeOffset.MinValue;

    public bool HoldingAfterBuffLoss { get; set; }

    public bool RerollWaitStarted { get; set; }

    public bool OnRerollPool { get; set; }

    public bool HasOpenedChest { get; set; }

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
        ElixirAttempts = 0;
        ElixirHintOrigin = null;
        HintsApplied = 0;
        WaitingForSpawnSince = DateTimeOffset.MinValue;
        PhaseStartedUtc = DateTimeOffset.UtcNow;
    }

    public void SeedPool(IEnumerable<PotTreasureCandidate> all)
    {
        Pool.Clear();
        Pool.AddRange(all);
    }

    public void NarrowTo(IEnumerable<PotTreasureCandidate> survivors)
    {
        Candidates.Clear();
        foreach (PotTreasureCandidate c in survivors)
        {
            Candidates.Enqueue(c);
        }

        CandidateTotal = Candidates.Count;
        HintsApplied++;
        ElixirAttempts = 0;
        ElixirHintOrigin = null;
        SettledAtUtc = DateTimeOffset.MinValue;
        Phase = PotChestFarmPhase.SearchingCandidates;
        PhaseStartedUtc = DateTimeOffset.UtcNow;
    }
}

public sealed class GoalPathStepMemory(IGoal goal, IPathCalculator calculator, bool pauseWhenPlanCompletes = false)
{
    private Task<PathCalculationResult>? pathStepTask = calculator.Calculate(goal);

    private bool emptyPlan;

    private bool routingFailed;

    public bool PauseWhenPlanCompletes { get; } = pauseWhenPlanCompletes;

    public Queue<IPathStep> PathSteps { get; private set; } = [];

    public bool IsEmptyPlan => emptyPlan && pathStepTask == null;

    public bool RoutingFailed => routingFailed && pathStepTask == null;

    public bool IsValid => pathStepTask != null || PathSteps.Count != 0 || emptyPlan || routingFailed;

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
            PathCalculationResult result = pathStepTask.Result;
            PathSteps = result.Steps;
            routingFailed = result.RoutingFailed;
            emptyPlan = PathSteps.Count == 0 && !result.RoutingFailed;
        }
        else
        {
            routingFailed = true;
            emptyPlan = false;
            PathSteps = [];
        }

        pathStepTask = null;
    }

    public IPathStep? GetNextPathStep() => PathSteps.Count > 0 && PathSteps.TryPeek(out IPathStep? step) ? step : null;

    public bool IsApproachingAethernetTeleport()
    {
        if (PathSteps.Count == 0)
        {
            return false;
        }

        using IEnumerator<IPathStep> e = PathSteps.GetEnumerator();
        if (!e.MoveNext())
        {
            return false;
        }

        if (e.Current.Kind == PathStepKind.Teleport)
        {
            return true;
        }

        return e.Current.Kind == PathStepKind.Pathfind
               && e.MoveNext()
               && e.Current.Kind == PathStepKind.Teleport;
    }

    public void DequeuePathStep()
    {
        if (PathSteps.Any())
        {
            PathSteps.Dequeue();
        }
    }
}
