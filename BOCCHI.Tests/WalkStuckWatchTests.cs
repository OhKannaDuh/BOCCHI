using BOCCHI.Treasure.Hunt;
using Xunit;

namespace BOCCHI.Tests;

public class WalkStuckWatchTests
{
    [Fact]
    public void NoProgressNudgesThenRepathsThenGivesUp()
    {
        WalkStuckWatch watch = new(new(TimeSpan.Zero, TimeSpan.Zero, 1));
        Assert.Equal(WalkStuckWatch.Action.None, watch.Tick(1, 10));
        Assert.Equal(WalkStuckWatch.Action.Nudge, watch.Tick(1, 10));
        Assert.True(watch.NudgeIssued);
        Assert.Equal(WalkStuckWatch.Action.Repath, watch.Tick(1, 10));
        Assert.False(watch.NudgeIssued);
        Assert.Equal(WalkStuckWatch.Action.Nudge, watch.Tick(1, 10));
        Assert.Equal(WalkStuckWatch.Action.GiveUp, watch.Tick(1, 10));
        Assert.Equal(WalkStuckWatch.Action.None, watch.Tick(1, 10));
    }

    [Fact]
    public void ProgressRestartsTheNudgeTimer()
    {
        WalkStuckWatch watch = new(new(TimeSpan.FromMilliseconds(40), TimeSpan.FromSeconds(1), 1));
        watch.Tick(1, 10);
        Thread.Sleep(60);
        Assert.Equal(WalkStuckWatch.Action.Nudge, watch.Tick(1, 10));
        Assert.Equal(WalkStuckWatch.Action.None, watch.Tick(1, 8));
        Assert.False(watch.NudgeIssued);
        Assert.Equal(WalkStuckWatch.Action.None, watch.Tick(1, 8));
        Thread.Sleep(60);
        Assert.Equal(WalkStuckWatch.Action.Nudge, watch.Tick(1, 8));
    }

    [Fact]
    public void NoProgressWaitsForNudgeAndEscalationDeadlines()
    {
        WalkStuckWatch watch = new(new(TimeSpan.FromMilliseconds(30), TimeSpan.FromMilliseconds(120), 1));
        Assert.Equal(WalkStuckWatch.Action.None, watch.Tick(1, 10));
        Assert.Equal(WalkStuckWatch.Action.None, watch.Tick(1, 10));
        Thread.Sleep(50);
        Assert.Equal(WalkStuckWatch.Action.Nudge, watch.Tick(1, 10));
        Assert.Equal(WalkStuckWatch.Action.None, watch.Tick(1, 10));
        Thread.Sleep(90);
        Assert.Equal(WalkStuckWatch.Action.Repath, watch.Tick(1, 10));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ResetOrNewKeyClearsEscalations(bool reset)
    {
        WalkStuckWatch watch = new(new(TimeSpan.Zero, TimeSpan.Zero, 1));
        watch.Tick(1, 10);
        watch.Tick(1, 10);
        Assert.Equal(WalkStuckWatch.Action.Repath, watch.Tick(1, 10));
        if (reset)
        {
            watch.Reset();
        }
        long key = reset ? 1 : 2;
        Assert.Equal(WalkStuckWatch.Action.None, watch.Tick(key, 20));
        Assert.Equal(WalkStuckWatch.Action.Nudge, watch.Tick(key, 20));
        Assert.Equal(WalkStuckWatch.Action.Repath, watch.Tick(key, 20));
    }

    [Fact]
    public void RestartProgressForgetsDistanceButKeepsEscalations()
    {
        WalkStuckWatch watch = new(new(TimeSpan.Zero, TimeSpan.Zero, 1));
        watch.Tick(1, 10);
        watch.Tick(1, 10);
        Assert.Equal(WalkStuckWatch.Action.Repath, watch.Tick(1, 10));
        watch.RestartProgress();
        Assert.Equal(WalkStuckWatch.Action.None, watch.Tick(1, 100));
        Assert.False(watch.NudgeIssued);
        Assert.Equal(WalkStuckWatch.Action.Nudge, watch.Tick(1, 100));
        Assert.Equal(WalkStuckWatch.Action.GiveUp, watch.Tick(1, 100));
    }

    [Fact]
    public void PathfindingNeverIssuesAnAction()
    {
        WalkStuckWatch watch = new(new(TimeSpan.Zero, TimeSpan.Zero, 1));
        Assert.Equal(WalkStuckWatch.Action.None, watch.Tick(1, 10, true));
        Assert.Equal(WalkStuckWatch.Action.None, watch.Tick(1, 10, true));
        Assert.Equal(WalkStuckWatch.Action.Nudge, watch.Tick(1, 10));
        Assert.Equal(WalkStuckWatch.Action.None, watch.Tick(1, 10, true));
        Assert.Equal(WalkStuckWatch.Action.None, watch.Tick(2, 10, true));
    }

    [Fact]
    public void EscalationWaitsUntilItsDeadlineAndCanBeCappedAfterNudge()
    {
        WalkStuckWatch watch = new(new(TimeSpan.Zero, TimeSpan.FromSeconds(10), 1));
        watch.Tick(1, 10);
        watch.CapEscalateAfter(TimeSpan.FromMilliseconds(1));
        Assert.Equal(WalkStuckWatch.Action.Nudge, watch.Tick(1, 10));
        Thread.Sleep(20);
        Assert.Equal(WalkStuckWatch.Action.None, watch.Tick(1, 10));
        watch.CapEscalateAfter(TimeSpan.Zero);
        watch.CapEscalateAfter(TimeSpan.FromMilliseconds(-1));
        Assert.Equal(WalkStuckWatch.Action.None, watch.Tick(1, 10));
        watch.CapEscalateAfter(TimeSpan.FromMilliseconds(20));
        watch.CapEscalateAfter(TimeSpan.FromSeconds(20));
        Thread.Sleep(40);
        Assert.Equal(WalkStuckWatch.Action.Repath, watch.Tick(1, 10));
    }
}
