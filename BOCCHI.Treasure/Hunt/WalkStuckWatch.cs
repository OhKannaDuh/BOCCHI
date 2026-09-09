namespace BOCCHI.Treasure.Hunt;

/// <summary>
///     Shared approach stuck watch: progress resets the clock; after <see cref="Options.NudgeAfter"/>
///     issue a lateral nudge; after <see cref="Options.EscalateAfter"/> either give up or repath
///     (Treasure: escalate = give up; Carrot: escalate = repath until MaxEscalations).
/// </summary>
public sealed class WalkStuckWatch
{
    public enum Action
    {
        None,
        Nudge,
        Repath,
        GiveUp,
    }

    public readonly record struct Options(
        TimeSpan NudgeAfter,
        TimeSpan EscalateAfter,
        int MaxEscalations,
        float ProgressThreshold = 1.5f);

    private readonly Options options;
    private long? key;
    private float bestDistance = float.MaxValue;
    private DateTime startedUtc = DateTime.MinValue;
    private bool nudgeIssued;
    private int escalationCount;

    public WalkStuckWatch(Options options) => this.options = options;

    public bool NudgeIssued => nudgeIssued;

    public void Reset()
    {
        key = null;
        bestDistance = float.MaxValue;
        startedUtc = DateTime.MinValue;
        nudgeIssued = false;
        escalationCount = 0;
    }

    /// <summary>
    ///     After a nudge that was intentionally skipped (Hide / dense pack), do not wait the
    ///     full escalate window while vnav is stopped — give up within <paramref name="maxRemaining"/>.
    /// </summary>
    public void CapEscalateAfter(TimeSpan maxRemaining)
    {
        if (!nudgeIssued || key == null || maxRemaining <= TimeSpan.Zero)
        {
            return;
        }

        DateTime now = DateTime.UtcNow;
        DateTime naturalEscalate = startedUtc + options.EscalateAfter;
        DateTime capped = now + maxRemaining;
        if (naturalEscalate > capped)
        {
            startedUtc = capped - options.EscalateAfter;
        }
    }

    /// <param name="pathfinding">True while vnav is still computing — do not count as stuck.</param>
    public Action Tick(long watchKey, float distance, bool pathfinding = false)
    {
        DateTime now = DateTime.UtcNow;

        if (key != watchKey)
        {
            key = watchKey;
            bestDistance = distance;
            startedUtc = now;
            nudgeIssued = false;
            escalationCount = 0;
            return Action.None;
        }

        if (pathfinding)
        {
            return Action.None;
        }

        if (distance < bestDistance - options.ProgressThreshold)
        {
            bestDistance = distance;
            startedUtc = now;
            nudgeIssued = false;
            return Action.None;
        }

        if (!nudgeIssued && now - startedUtc >= options.NudgeAfter)
        {
            nudgeIssued = true;
            return Action.Nudge;
        }

        if (now - startedUtc < options.EscalateAfter)
        {
            return Action.None;
        }

        // Treasure: MaxEscalations 0 → GiveUp on first escalate.
        // Carrot: MaxEscalations 2 → Repath twice, then GiveUp.
        if (options.MaxEscalations <= 0)
        {
            Reset();
            return Action.GiveUp;
        }

        // Carrot only escalates after a nudge was issued.
        if (!nudgeIssued)
        {
            return Action.None;
        }

        escalationCount++;
        if (escalationCount > options.MaxEscalations)
        {
            Reset();
            return Action.GiveUp;
        }

        startedUtc = now;
        nudgeIssued = false;
        return Action.Repath;
    }
}
