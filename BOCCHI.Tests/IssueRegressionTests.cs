using BOCCHI.Common.Data;
using System.Numerics;
using BOCCHI.Automator.Services.Paths;
using BOCCHI.Common.Data.Aethernet;
using BOCCHI.Common.Data.Zones;
using BOCCHI.Common.Services;
using BOCCHI.MobFarmer.Services;
using BOCCHI.Treasure.Hunt;
using Ocelot.Chain.Middleware.Step;
using Ocelot.Rotation.Services;
using Ocelot.Services.Gate;
using Xunit;

namespace BOCCHI.Tests;

// One test per problem that reached players or was found in review, named after what went wrong.
public class IssueRegressionTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 21, 12, 0, 0, TimeSpan.Zero);

    private static readonly AutoReviveState Dead = new(
        AcceptRaises: true,
        ReturnToCamp: true,
        ReturnDelaySeconds: 60,
        PromptOpen: true,
        RaisePending: false,
        PromptIsReturn: false,
        InForkedTower: false,
        ProtectedContent: false,
        SawAlive: true,
        DeadFor: TimeSpan.FromSeconds(90));

    // Discord: sat on the mount and died when a mob attacked (Ziik, Ryuzo).
    [Theory]
    [InlineData(15, 12, true)]
    [InlineData(15, 15, true)]
    [InlineData(15, 16, false)]
    [InlineData(null, 30, true)]
    [InlineData(15, null, true)]
    public void AnAttackerIsFoughtOnlyWhenItIsNotAboveYourLevel(int? yours, int? attacker, bool fight)
    {
        Assert.Equal(fight, CombatRules.CanFight(yours, attacker));
    }

    // Discord: a stronger mob that keeps chasing has to be fought in the end (pot chests, hunts).
    [Fact]
    public void AMobYouCannotOutrunIsFoughtAfterTenSeconds()
    {
        OutrunTimer timer = new();

        Assert.False(timer.Update(true, Start));
        Assert.False(timer.Update(true, Start.AddSeconds(9)));
        Assert.True(timer.Update(true, Start.AddSeconds(10)));
    }

    [Fact]
    public void LeavingCombatRestartsTheOutrunCountdown()
    {
        OutrunTimer timer = new();
        timer.Update(true, Start);
        timer.Update(false, Start.AddSeconds(8));

        Assert.False(timer.Update(true, Start.AddSeconds(12)));
        Assert.True(timer.Update(true, Start.AddSeconds(22)));
    }

    // Discord (croizat): a raise was not accepted.
    [Fact]
    public void APendingRaiseIsAccepted()
    {
        (AutoReviveAction action, _) = AutoReviveRule.Decide(Dead with { RaisePending = true, DeadFor = TimeSpan.FromSeconds(3) });

        Assert.Equal(AutoReviveAction.AcceptRaise, action);
    }

    [Fact]
    public void ARaiseIsNotAcceptedWhenThatSettingIsOff()
    {
        (AutoReviveAction action, _) = AutoReviveRule.Decide(Dead with { RaisePending = true, AcceptRaises = false });

        Assert.Equal(AutoReviveAction.Nothing, action);
    }

    // Discord (Ziik): an automatic return throws you out of Forked Tower or a CE.
    [Fact]
    public void YouAreNeverReturnedToCampFromForkedTowerOrACriticalEncounter()
    {
        (AutoReviveAction action, _) = AutoReviveRule.Decide(Dead with { ProtectedContent = true });

        Assert.Equal(AutoReviveAction.Nothing, action);
    }

    // Owner decision 2026-11-22: no reviving at all in Forked Tower; raises are accepted in FATEs and CEs.
    [Fact]
    public void NothingIsClickedInsideForkedTower()
    {
        Assert.Equal(AutoReviveAction.Nothing, AutoReviveRule.Decide(Dead with { InForkedTower = true, RaisePending = true }).Action);
        Assert.Equal(AutoReviveAction.Nothing, AutoReviveRule.Decide(Dead with { InForkedTower = true }).Action);
    }

    [Fact]
    public void ARaiseIsAcceptedInsideACriticalEncounter()
    {
        (AutoReviveAction action, _) = AutoReviveRule.Decide(Dead with { RaisePending = true, ProtectedContent = true });

        Assert.Equal(AutoReviveAction.AcceptRaise, action);
    }

    // Second opinion: the guard was off when the plugin loaded while you were already dead.
    [Fact]
    public void YouAreNotReturnedIfThePluginNeverSawYouAlive()
    {
        (AutoReviveAction action, _) = AutoReviveRule.Decide(Dead with { SawAlive = false });

        Assert.Equal(AutoReviveAction.Nothing, action);
    }

    [Fact]
    public void ReturnWaitsForTheDelayThenHappens()
    {
        Assert.Equal(AutoReviveAction.Nothing, AutoReviveRule.Decide(Dead with { DeadFor = TimeSpan.FromSeconds(59) }).Action);
        Assert.Equal(AutoReviveAction.ReturnToCamp, AutoReviveRule.Decide(Dead with { DeadFor = TimeSpan.FromSeconds(60) }).Action);
    }

    // Second opinion: with a raise pending, a return prompt must not be answered as if it were the raise.
    [Fact]
    public void AReturnPromptIsNotTreatedAsARaise()
    {
        (AutoReviveAction action, _) = AutoReviveRule.Decide(
            Dead with { RaisePending = true, PromptIsReturn = true, ProtectedContent = true });

        Assert.Equal(AutoReviveAction.Nothing, action);
    }

    [Fact]
    public void NothingIsClickedWithoutAPrompt()
    {
        Assert.Equal(AutoReviveAction.Nothing, AutoReviveRule.Decide(Dead with { PromptOpen = false }).Action);
    }

    // Audit 76: a dip that lasted two reads (stack moved, inventory re-sync) was counted as income when it came back.
    [Fact]
    public void ACurrencyDipOverSeveralReadsIsNotCountedAsIncome()
    {
        DeltaRateTracker tracker = new(() => TimeSpan.FromMinutes(5));
        tracker.SetCounting(true);
        foreach (long read in new long[] { 5000, 3000, 3000, 5000 })
        {
            tracker.RecordPositiveDelta(read);
        }

        Assert.Equal(0, tracker.GetHistory(TimeSpan.FromSeconds(15)).Sum());

        tracker.RecordPositiveDelta(5100);
        // History holds per-hour rates per 15 s bucket: 100 in one bucket is 100 / 15 s * 3600.
        Assert.Equal(100f / 15 * 3600, tracker.GetHistory(TimeSpan.FromSeconds(15)).Sum(), 1);
    }

    // The party-invite box must never be mistaken for the return box.
    [Fact]
    public void APromptOnlyMatchesItsOwnTemplate()
    {
        const string invite = "Join <string(lstr1)>'s party?";

        Assert.True(ReturnYesNo.MatchesTemplate("Join Some Player's party?", invite));
        Assert.False(ReturnYesNo.MatchesTemplate("Return to the base camp?", invite));
        Assert.True(ReturnYesNo.MatchesTemplate("Return to the base camp?", "Return to the base camp?"));
    }

    // Discord (Godfrey): mounted up again just to walk the last few yalms to a pot.
    [Fact]
    public void NoMountForAShortHopNextToAPot()
    {
        Vector3 pot = new(100f, 0f, 100f);

        Assert.True(PathMounting.IsShortHopNearPot(new(110f, 0f, 100f), new(95f, 0f, 100f), [pot], 30f));
        Assert.False(PathMounting.IsShortHopNearPot(new(300f, 0f, 100f), new(95f, 0f, 100f), [pot], 30f));
        Assert.False(PathMounting.IsShortHopNearPot(new(110f, 0f, 100f), new(95f, 0f, 100f), [], 30f));
    }

    // Discord (Godfrey): the pot waiting spots were too far from the pot.
    [Fact]
    public void ThePotWaitingRingStaysCloseToThePot()
    {
        Assert.True(NavigationConstants.PotPrepositionMaxRadius <= 20f);
    }

    // Discord (Ziik): Mob Farmer waited 20-30 seconds between pulls with fewer enemies than the minimum.
    [Theory]
    [InlineData(0, 3, 60, false)]
    [InlineData(3, 3, 0, true)]
    [InlineData(1, 3, 14, false)]
    [InlineData(1, 3, 15, true)]
    public void MobFarmerStartsAPullAtTheMinimumOrAfterTheTimeout(int free, int minimum, int waitedSeconds, bool start)
    {
        Assert.Equal(
            start,
            MobFarmerRules.ShouldStartPull(free, minimum, TimeSpan.FromSeconds(waitedSeconds), TimeSpan.FromSeconds(15)));
    }

    // Audit: an enemy Mob Farmer gave up on was ignored for the rest of the session.
    [Fact]
    public void AnEnemyMobFarmerGaveUpOnIsTriedAgainLater()
    {
        SkipList skips = new(TimeSpan.FromMinutes(5));
        DateTime now = Start.UtcDateTime;
        skips.Skip(42, now);

        Assert.True(skips.IsSkipped(42, now.AddMinutes(4)));
        Assert.False(skips.IsSkipped(42, now.AddMinutes(5)));
        Assert.False(skips.IsSkipped(7, now));
    }

    // Discord (Ziik): the hunt kept getting stuck on the Wanderer's Haven ledge coffer.
    [Fact]
    public void TheLedgeCofferInSouthHornIsSkipped()
    {
        Assert.True(TreasureHuntPathOverrides.IsUnreachable(ZoneId.SouthHorn, 1842));
    }

    // Discord (Dead): with combat set to None, Rotation Solver was left switched on.
    [Fact]
    public void TearingDownTheRotationSwitchesItOffAndForgetsIt()
    {
        RecordingRotation rotation = new();
        CombatRotationSession session = new([rotation], [], null!, new QuietLog<CombatRotationSession>());
        session.Prepare(new CombatRotationRecipe(JobRotationBackendKind.RotationSolverReborn, CombatAiKind.None));
        session.Enable(CombatActivity.Fate);

        session.Teardown();
        session.Enable(CombatActivity.Fate);

        Assert.False(session.ActiveRecipe.IsActive);
        Assert.Equal(["prepare", "enable", "teardown"], rotation.Calls);
    }

    // Audit: a frame longer than a second lost its whole seconds, so timed jobs ran late.
    [Fact]
    public void ALongFrameCountsInFull()
    {
        long left = GateService.Advance(0, TimeSpan.FromMilliseconds(1500), 1000, out bool due);

        Assert.True(due);
        Assert.Equal(TimeSpan.FromMilliseconds(500).Ticks, left);
    }

    // Audit: frames were cut to whole milliseconds, so limits ran slow at high frame rates.
    [Fact]
    public void HighFrameRatesKeepTime()
    {
        long elapsed = 0;
        int frames = 0;
        bool due = false;
        while (!due)
        {
            elapsed = GateService.Advance(elapsed, TimeSpan.FromSeconds(1.0 / 144), 500, out due);
            frames++;
        }

        // 72 frames is exactly 500 ms; one more covers the tick rounding of 1/144 s. Whole milliseconds needed 84.
        Assert.InRange(frames, 72, 73);
    }

    // Audit: retry defaults of "no delay, forever" would freeze the game on a failing step.
    [Fact]
    public void RetryHasSafeDefaults()
    {
        RetryStepMiddleware retry = new();

        Assert.True(retry.DelayMs > 0);
        Assert.InRange(retry.MaxAttempts, 1, 1000);
    }

    private sealed class RecordingRotation : IJobRotationBackend
    {
        public List<string> Calls { get; } = [];

        public JobRotationBackendKind Kind => JobRotationBackendKind.RotationSolverReborn;

        public void Prepare(JobRotationSessionOptions options) => Calls.Add("prepare");

        public void Enable(CombatActivity activity) => Calls.Add("enable");

        public void Disable() => Calls.Add("disable");

        public void Refresh()
        {
        }

        public void Teardown() => Calls.Add("teardown");
    }
}
