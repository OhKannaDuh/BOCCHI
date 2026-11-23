using System.Numerics;
using BOCCHI.Common.Config;
using BOCCHI.Common.Data.Mobs;
using BOCCHI.Common.Data.Paths;
using BOCCHI.Common.Data.Zones.Graph;
using BOCCHI.Common.Data.Zones.Graph.Traversal;
using BOCCHI.Common.Services.Logging;
using Ocelot.Chain;
using Ocelot.Chain.Middleware.Step;
using Newtonsoft.Json;
using Ocelot.Services.Pathfinding;
using Ocelot.UI;
using Xunit;

namespace BOCCHI.Tests;

public class DecisionTests
{
    [Fact]
    public void OldSavedMobsAreSwappedForTheirNewIds()
    {
        List<Mob> mobs = [Mob.Goobbue, Mob.Taurus, Mob.Taurus2];

        MobData.ReplaceLegacyMobs(mobs);

        Assert.Equal([Mob.Goobbue2, Mob.Taurus2], mobs);
    }

    [Theory]
    [InlineData(75, 74, false)]
    [InlineData(75, 75, true)]
    [InlineData(75, 100, true)]
    [InlineData(0, 100, false)]
    public void FateIsSkippedAtOrAboveTheProgressCutoff(int cutoff, byte progress, bool skipped)
    {
        FatesConfig config = new() { MaxFateProgressPercent = cutoff };

        Assert.Equal(skipped, config.ShouldSkipByProgress(progress));
    }

    [Fact]
    public void ADisabledPotIsStillDoneWhilePotsArePreferred()
    {
        FatesConfig config = new() { DisabledFateIds = [1976] };

        Assert.False(config.IsFateEnabledForIllegalMode(1976, isPotFate: true, preferPotFates: false));
        Assert.True(config.IsFateEnabledForIllegalMode(1976, isPotFate: true, preferPotFates: true));
        Assert.False(config.IsFateEnabledForIllegalMode(1976, isPotFate: false, preferPotFates: true));
    }

    [Fact]
    public void PotTimingOnlyAppliesWhenAPotSettingIsOnAndThePotIsKnown()
    {
        FatesConfig config = new();

        Assert.False(config.IsPotFallbackGatingEnabled(1976, false, false, false));
        Assert.False(config.IsPotFallbackGatingEnabled(0, false, true, true));
        Assert.True(config.IsPotFallbackGatingEnabled(1976, false, true, false));
        Assert.True(config.IsPotFallbackGatingEnabled(1976, false, false, true));
    }

    [Theory]
    [InlineData(2, 119, true)]
    [InlineData(2, 120, false)]
    [InlineData(2, 0, false)]
    [InlineData(0, 30, false)]
    public void APotWithTooLittleTimeLeftIsSkipped(int minimumMinutes, long secondsLeft, bool skipped)
    {
        PotsConfig config = new() { MinPotFateMinutesRemaining = minimumMinutes };

        Assert.Equal(skipped, config.ShouldSkipLivePot(secondsLeft));
    }

    [Fact]
    public void TheThemeSettingSurvivesSaveAndLoad()
    {
        try
        {
            string saved = JsonConvert.SerializeObject(new UIConfig { UseBocchiTheme = false });
            OcelotTheme.Enabled = true;

            JsonConvert.DeserializeObject<UIConfig>(saved);

            Assert.False(OcelotTheme.Enabled);
        }
        finally
        {
            OcelotTheme.Enabled = true;
        }
    }

    [Fact]
    public async Task TheCheapestRouteWins()
    {
        GraphTraverser traverser = new(null!, null!, new QuietLog<DecisionTests>());
        traverser.AddCalculator(new FixedRoute("walk", 300f, PathStep.Pathfind(Vector3.One)));
        traverser.AddCalculator(new FixedRoute("return", 120f, PathStep.Return()));

        List<PathStep> steps = await traverser.FindPath(Vector3.Zero, new Node());

        Assert.Equal(PathStepKind.Return, Assert.Single(steps).Kind);
    }

    [Fact]
    public async Task ARouteThatCannotBeatTheBestIsNotCalculated()
    {
        FixedRoute expensive = new("direct", 500f, PathStep.Pathfind(Vector3.One), lowerBound: 400f);
        FixedRoute needed = new("shortcut", 80f, PathStep.Pathfind(Vector3.One), lowerBound: 50f);
        GraphTraverser traverser = new(null!, null!, new QuietLog<DecisionTests>());
        traverser.AddCalculator(expensive);
        traverser.AddCalculator(needed);
        traverser.AddCalculator(new FixedRoute("return", 120f, PathStep.Return()));

        List<PathStep> steps = await traverser.FindPath(Vector3.Zero, new Node());

        Assert.False(expensive.Calculated);
        Assert.True(needed.Calculated);
        Assert.Equal(PathStepKind.Pathfind, Assert.Single(steps).Kind);
    }

    [Fact]
    public async Task NoRouteGivesNoSteps()
    {
        GraphTraverser traverser = new(null!, null!, new QuietLog<DecisionTests>());
        traverser.AddCalculator(new FixedRoute("none", null));

        Assert.Empty(await traverser.FindPath(Vector3.Zero, new Node()));
    }

    [Fact]
    public void TheSameLogLineInARowIsCountedNotRepeated()
    {
        BocchiLogBuffer buffer = new();

        buffer.Append(BocchiLogLevel.Info, "Chose FATE 1");
        buffer.Append(BocchiLogLevel.Info, "Chose FATE 1");
        buffer.Append(BocchiLogLevel.Info, "Chose FATE 2");

        IReadOnlyList<BocchiLogEntry> entries = buffer.Snapshot();
        Assert.Equal(2, entries.Count);
        Assert.Equal(2, entries[0].Count);
    }

    [Fact]
    public void CopyAllStartsWithTheHeaderAndLeavesOutVerboseLines()
    {
        BocchiLogBuffer buffer = new();
        buffer.Append(BocchiLogLevel.Verbose, "Step started");
        buffer.Append(BocchiLogLevel.Warning, "Path step failed");

        string copied = buffer.FormatAllForClipboard("BOCCHI 1.2.3");

        Assert.StartsWith("BOCCHI 1.2.3", copied);
        Assert.Contains("Path step failed", copied);
        Assert.DoesNotContain("Step started", copied);
    }

    [Fact]
    public async Task ARetriedStepStopsAtTheFirstSuccess()
    {
        int calls = 0;
        RetryStepMiddleware retry = new() { DelayMs = 0, MaxAttempts = 5 };

        StepResult result = await retry.InvokeAsync(
            new ChainContext("test", new NoServices()),
            new NamedStep(),
            () => Task.FromResult(++calls < 3 ? StepResult.Failure("not yet") : StepResult.Success()));

        Assert.True(result.IsSuccess);
        Assert.Equal(3, calls);
    }

    [Fact]
    public async Task ARetriedStepGivesUpAfterItsAttempts()
    {
        int calls = 0;
        RetryStepMiddleware retry = new() { DelayMs = 0, MaxAttempts = 4 };

        StepResult result = await retry.InvokeAsync(
            new ChainContext("test", new NoServices()),
            new NamedStep(),
            () =>
            {
                calls++;
                return Task.FromResult(StepResult.Failure("never"));
            });

        Assert.False(result.IsSuccess);
        Assert.Equal(4, calls);
    }

    private sealed class FixedRoute(string key, float? cost, PathStep? step = null, float? lowerBound = null)
        : IGraphCandidateCalculator
    {
        public bool Calculated { get; private set; }

        public string Key() => key;

        public float? LowerBoundCost(ZoneGraph graph, Vector3 start, Node goal) => lowerBound;

        public Task<TraversalCandidate?> CalculateAsync(ZoneGraph graph, Vector3 start, Node goal, IPathfinder pathfinder)
        {
            Calculated = true;
            return Task.FromResult(cost is { } value ? new TraversalCandidate(value, [step!]) : null);
        }
    }

    private sealed class NamedStep : IStep
    {
        public Task<StepResult> ExecuteAsync(IChainContext context) => Task.FromResult(StepResult.Success());
    }

    private sealed class NoServices : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }
}
