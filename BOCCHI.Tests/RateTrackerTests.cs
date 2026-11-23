using BOCCHI.Common.Data;
using Xunit;

namespace BOCCHI.Tests;

public class RateTrackerTests
{
    // Audit: rate uses active elapsed time and sums multiple readings.
    [Fact]
    public void PerHourUsesKnownReadingsAndExcludesPausedTime()
    {
        DateTime now = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        DeltaRateTracker tracker = new(() => TimeSpan.FromMinutes(5), () => now);
        tracker.SyncBaseline(100);
        tracker.SetCounting(true);
        now = now.AddSeconds(10);
        tracker.RecordPositiveDelta(110);
        Assert.Equal(0, tracker.PerHour);
        now = now.AddSeconds(20);
        tracker.RecordPositiveDelta(130);
        Assert.Equal(3600, tracker.PerHour);
        tracker.SetCounting(false);
        now = now.AddMinutes(1);
        Assert.Equal(3600, tracker.PerHour);
        tracker.SetCounting(true);
        now = now.AddSeconds(30);
        Assert.Equal(1800, tracker.PerHour);
    }

    // Audit: history places summed gains in the right buckets and excludes older gains.
    [Fact]
    public void HistoryBucketsSumGainsAndExcludeReadingsOutsideWindow()
    {
        DateTime now = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        DeltaRateTracker tracker = new(() => TimeSpan.FromSeconds(60), () => now);
        tracker.SyncBaseline(0);
        tracker.RecordPositiveDelta(100);
        now = now.AddSeconds(16);
        tracker.RecordPositiveDelta(110);
        now = now.AddSeconds(4);
        tracker.RecordPositiveDelta(115);
        now = now.AddSeconds(26);
        tracker.RecordPositiveDelta(145);
        now = now.AddSeconds(15);
        Assert.Equal(new float[] { 0, 3600, 0, 7200 }, tracker.GetHistory(TimeSpan.FromSeconds(15)));
    }

    // Audit: the hourly rate drops readings older than one hour.
    [Fact]
    public void PerHourPrunesReadingsOlderThanOneHour()
    {
        DateTime now = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        DeltaRateTracker tracker = new(() => TimeSpan.FromMinutes(5), () => now);
        tracker.SetCounting(true);
        tracker.SyncBaseline(0);
        now = now.AddSeconds(1);
        tracker.RecordPositiveDelta(100);
        now = now.AddHours(1).AddSeconds(1);
        tracker.RecordPositiveDelta(150);
        Assert.Equal(50, tracker.PerHour);
    }
}
