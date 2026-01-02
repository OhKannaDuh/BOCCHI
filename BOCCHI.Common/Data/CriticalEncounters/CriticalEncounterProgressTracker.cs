namespace BOCCHI.Common.Data.CriticalEncounters;

public class ProgressEstimate
{
    public DateTimeOffset? CompletionTime { get; set; }

    public double RSquared { get; set; }

    public TimeSpan Time
    {
        get => CompletionTime?.Subtract(DateTime.Now) ?? TimeSpan.Zero;
    }
}

public readonly record struct ProgressEntry(DateTimeOffset Timestamp, byte Progress);

public sealed class CriticalEncounterProgressTracker
{
    private byte previous = 0;

    private byte first = 0;

    private DateTimeOffset Start = DateTimeOffset.MinValue;

    private List<ProgressEntry> entries = [];

    public void Observe(CriticalEncounter criticalEncounter)
    {
        var current = criticalEncounter.Progress;
        if (current <= previous)
        {
            return;
        }

        previous = current;
        if (first == 0)
        {
            first = current;
            Start = DateTimeOffset.Now;
        }

        entries.Add(new ProgressEntry(DateTimeOffset.Now, current));
    }

    public ProgressEstimate? Estimate()
    {
        if (entries.Count < 3)
        {
            return null;
        }

        var x = entries.Select(p => (p.Timestamp - entries[0].Timestamp).TotalMinutes).ToArray();
        var y = entries.Select(p => (double)p.Progress).ToArray();

        var xMean = x.Average();
        var yMean = y.Average();

        var numerator = x.Zip(y, (xi, yi) => (xi - xMean) * (yi - yMean)).Sum();
        var denominator = x.Sum(xi => Math.Pow(xi - xMean, 2));

        var slope = numerator / denominator;
        var intercept = yMean - slope * xMean;

        var ssTotal = y.Sum(yi => Math.Pow(yi - yMean, 2));
        var ssResidual = x.Zip(y, (xi, yi) => Math.Pow(yi - (slope * xi + intercept), 2)).Sum();
        var rSquared = 1 - ssResidual / ssTotal;

        if (slope <= 0)
        {
            return new ProgressEstimate { CompletionTime = null, RSquared = rSquared };
        }

        var minutesTo100 = (100 - intercept) / slope;
        var completionTime = entries[0].Timestamp.AddMinutes(minutesTo100);

        return new ProgressEstimate
        {
            CompletionTime = completionTime,
            RSquared = rSquared,
        };
    }
}
