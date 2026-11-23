using BOCCHI.Common.Data.SupportJobs;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using Xunit;

namespace BOCCHI.Tests;

[CollectionDefinition("Phantom job clock", DisableParallelization = true)]
public class PhantomJobClockCollection;

[Collection("Phantom job clock")]
public class PhantomJobChangeGateTests : IDisposable
{
    private DateTimeOffset now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private readonly Func<DateTimeOffset> previousClock = PhantomJobChangeGate.Now;

    public PhantomJobChangeGateTests()
    {
        PhantomJobChangeGate.Now = () => now;
        PhantomJobChangeGate.Track(false);
        PhantomJobChangeGate.NoteCombatEnded();
        now = now.AddSeconds(6);
    }

    // Audit: leaving combat blocks job changes for the full five-second settle period.
    [Fact]
    public void CombatExitBlocksUntilFiveSecondsHaveElapsed()
    {
        ICondition conditions = StandIn.For<ICondition>(s => s.Answers("get_Item", false));
        PhantomJobChangeGate.Track(true);
        PhantomJobChangeGate.Track(false);
        Assert.True(PhantomJobChangeGate.IsBlocked(conditions));
        now = now.AddMilliseconds(4999);
        Assert.True(PhantomJobChangeGate.IsBlocked(conditions));
        now = now.AddMilliseconds(1);
        Assert.False(PhantomJobChangeGate.IsBlocked(conditions));
    }

    // Audit: combat itself blocks job changes even after the settle deadline.
    [Fact]
    public void BeingInCombatBlocksJobChanges()
    {
        ICondition conditions = StandIn.For<ICondition>(s => s.Answers("get_Item", args => (ConditionFlag)args[0]! == ConditionFlag.InCombat));
        Assert.True(PhantomJobChangeGate.IsBlocked(conditions));
    }

    public void Dispose()
    {
        PhantomJobChangeGate.Track(false);
        now = DateTimeOffset.MinValue;
        PhantomJobChangeGate.NoteCombatEnded();
        PhantomJobChangeGate.Now = previousClock;
    }
}
