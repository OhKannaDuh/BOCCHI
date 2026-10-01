namespace BOCCHI.Common.Data;

public readonly record struct DeltaSnapshot(long Delta, DateTime Time);

public sealed class DeltaRateTracker(Func<TimeSpan> getTrackedWindow)
{
    public static readonly TimeSpan DefaultWindow = TimeSpan.FromMinutes(5);

    public static readonly TimeSpan DefaultGraphBucket = TimeSpan.FromSeconds(15);

    public static readonly TimeSpan MinElapsedForRate = TimeSpan.FromSeconds(20);

    public static readonly TimeSpan RateHour = TimeSpan.FromHours(1);

    private static readonly TimeSpan RecoveryWindow = TimeSpan.FromSeconds(2);

    private readonly List<DeltaSnapshot> snapshots = [];

    private long lastValue;

    private TimeSpan accumulatedActive = TimeSpan.Zero;

    private DateTime? activeStartedUtc;

    private long? valueBeforeDrop;

    private DateTime dropAt = DateTime.MinValue;

    public bool HasValue { get; private set; }

    public long LastValue => lastValue;

    public double PerHour
    {
        get
        {
            Prune();
            TimeSpan elapsed = ActiveElapsed();
            if (elapsed < MinElapsedForRate)
            {
                return 0;
            }

            long windowGained = SumSince(DateTime.UtcNow - RateHour);
            if (elapsed < RateHour)
            {
                return windowGained / elapsed.TotalHours;
            }

            return windowGained / RateHour.TotalHours;
        }
    }

    public void SetCounting(bool counting)
    {
        if (counting)
        {
            activeStartedUtc ??= DateTime.UtcNow;
            return;
        }

        if (activeStartedUtc is not { } started)
        {
            return;
        }

        accumulatedActive += DateTime.UtcNow - started;
        activeStartedUtc = null;
    }

    public void SyncBaseline(long value)
    {
        lastValue = value;
        HasValue = true;
        valueBeforeDrop = null;
        dropAt = DateTime.MinValue;
    }

    public void Reset()
    {
        snapshots.Clear();
        lastValue = 0;
        accumulatedActive = TimeSpan.Zero;
        activeStartedUtc = null;
        HasValue = false;
        valueBeforeDrop = null;
        dropAt = DateTime.MinValue;
    }

    public void RecordPositiveDelta(long current)
    {
        Prune();

        if (!HasValue)
        {
            lastValue = current;
            HasValue = true;
            return;
        }

        long delta = current - lastValue;

        if (delta < 0)
        {
            valueBeforeDrop = lastValue;
            dropAt = DateTime.UtcNow;
            lastValue = current;
            return;
        }

        if (valueBeforeDrop is { } before && DateTime.UtcNow - dropAt <= RecoveryWindow)
        {
            lastValue = current;
            valueBeforeDrop = null;
            if (current <= before)
            {
                return;
            }

            delta = current - before;
        }
        else
        {
            valueBeforeDrop = null;
            lastValue = current;
        }

        if (delta <= 0)
        {
            return;
        }

        snapshots.Add(new(delta, DateTime.UtcNow));
    }

    public float[] GetHistory(TimeSpan sampleDuration)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(sampleDuration, TimeSpan.Zero);
        Prune();

        TimeSpan duration = getTrackedWindow();
        DateTime now = DateTime.UtcNow;
        DateTime start = now - duration;

        List<DeltaSnapshot> relevant = snapshots
            .Where(s => s.Time >= start)
            .OrderBy(s => s.Time)
            .ToList();

        if (relevant.Count == 0)
        {
            return [];
        }

        int bucketCount = (int)Math.Floor(duration.TotalSeconds / sampleDuration.TotalSeconds);
        if (bucketCount <= 0)
        {
            return [];
        }

        double[] bucketTotals = new double[bucketCount];

        foreach (DeltaSnapshot snapshot in relevant)
        {
            double secondsFromStart = (snapshot.Time - start).TotalSeconds;
            int index = (int)(secondsFromStart / sampleDuration.TotalSeconds);

            if (index < 0 || index >= bucketCount)
            {
                continue;
            }

            bucketTotals[index] += snapshot.Delta;
        }

        float[] result = new float[bucketCount];
        double bucketSeconds = sampleDuration.TotalSeconds;

        for (int i = 0; i < bucketCount; i++)
        {
            double amount = bucketTotals[i];
            result[i] = amount <= 0 ? 0f : (float)(amount / bucketSeconds * 3600.0);
        }

        return result;
    }

    private TimeSpan ActiveElapsed()
    {
        TimeSpan extra = activeStartedUtc is { } started ? DateTime.UtcNow - started : TimeSpan.Zero;
        return accumulatedActive + extra;
    }

    private long SumSince(DateTime cutoff)
    {
        long sum = 0;
        foreach (DeltaSnapshot snapshot in snapshots)
        {
            if (snapshot.Time >= cutoff)
            {
                sum += snapshot.Delta;
            }
        }

        return sum;
    }

    private void Prune()
    {
        if (snapshots.Count == 0)
        {
            return;
        }

        DateTime cutoff = DateTime.UtcNow - RateHour;

        int index = 0;
        while (index < snapshots.Count && snapshots[index].Time < cutoff)
        {
            index++;
        }

        if (index > 0)
        {
            snapshots.RemoveRange(0, index);
        }
    }
}
