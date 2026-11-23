using BOCCHI.Automator.Data;
using BOCCHI.Automator.Services;
using BOCCHI.Automator.Services.Paths;
using BOCCHI.Automator.StateMachine.Handlers;
using BOCCHI.Common.Config;
using BOCCHI.Common.Data.Fates;
using BOCCHI.Common.Data.MobFarmer;
using BOCCHI.Common.Data.StateMemory;
using BOCCHI.Common.Data.Zones;
using BOCCHI.Common.Data.Zones.Graph.Traversal;
using BOCCHI.Common.Services;
using BOCCHI.Fates.Services;
using BOCCHI.MobFarmer.Data;
using BOCCHI.MobFarmer.Services;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Fates;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Plugin.Services;
using Ocelot.Services.Data;
using Ocelot.Services.Pathfinding;
using Ocelot.Services.PlayerState;
using Ocelot.Services.Translation;
using Ocelot.States;
using Ocelot.Windows;
using Xunit;

namespace BOCCHI.Tests;

// The behaviours that live inside game-facing code, run against stand-ins for the game.
public class GameBehaviourTests
{
    // Discord (croizat): a mob dragged to a pot / CE waiting spot killed you while you waited.
    [Fact]
    public void AMobThatFollowsYouToAWaitingSpotOutranksWaiting()
    {
        bool inCombat = true;
        AutomatorMemory memory = new();
        InCombatHandler handler = CombatHandler(memory, () => inCombat);

        memory.TryAdd<WaitingForPotFateMemory>();
        Assert.Equal(StatePriority.VeryHigh + 1, handler.GetScore());

        // The pot wait forgets its memory when combat takes over; the fight must not drop back below waiting.
        memory.Forget<WaitingForPotFateMemory>();
        Assert.Equal(StatePriority.VeryHigh + 1, handler.GetScore());

        inCombat = false;
        Assert.Equal(StatePriority.Never, handler.GetScore());

        inCombat = true;
        Assert.Equal(StatePriority.High, handler.GetScore());
    }

    [Fact]
    public void AFightHandedOverByTheTreasureHuntOutranksReturning()
    {
        AutomatorMemory memory = new();
        InCombatHandler handler = CombatHandler(memory, () => true);

        memory.TryAdd<TreasureHuntSelfDefenceMemory>();

        Assert.True(handler.GetScore() > StatePriority.VeryHigh);
    }

    // Discord (Ziik): the second pot chest search walked back to the first pot's spots.
    [Fact]
    public void LeftoverFirstPotSpotsAreDroppedOnceAChestIsOpened()
    {
        PotChestFarmMemory farm = PotChestFarmMemory.CreateSmart(new FateId(2073));
        farm.Candidates.Enqueue(default!);

        farm.DropFirstPotCandidates();
        Assert.Single(farm.Candidates);

        farm.HasOpenedChest = true;
        farm.DropFirstPotCandidates();
        Assert.Empty(farm.Candidates);

        farm.OnRerollPool = true;
        farm.Candidates.Enqueue(default!);
        farm.DropFirstPotCandidates();
        Assert.Single(farm.Candidates);
    }

    // Discord (Alho): Mob Farmer spent cooldowns and phantom actions before the pack was gathered.
    [Fact]
    public void MobFarmerOnlyRunsTheRotationToTagAnEnemyUntilTheFight()
    {
        FarmerPhase phase = FarmerPhase.Gathering;
        RecordingCombat combat = new();
        MobFarmerService farmer = Farmer(combat, () => phase);
        farmer.Toggle();
        combat.Calls.Clear();

        farmer.Update();
        Assert.Empty(combat.Calls);

        farmer.RequestAiPull();
        farmer.Update();
        Assert.Equal(["enable", "tick"], combat.Calls);

        combat.Calls.Clear();
        farmer.Update();
        Assert.Equal(["disable"], combat.Calls);

        combat.Calls.Clear();
        phase = FarmerPhase.Fighting;
        farmer.Update();
        Assert.Equal(["tick"], combat.Calls);
    }

    // Your log, 01:48:40 / 01:49:04: an extra Return fired right after the hunt was paused.
    [Fact]
    public void AQueuedReturnIsForgottenWhenAHuntTakesOver()
    {
        AutomatorMemory memory = new();
        memory.TryAdd(new ReturningStateMemory(TimeSpan.Zero));
        memory.TryAdd<WaitingForPotFateMemory>();

        IllegalModeActivityWork.ForgetLatchesForHunt(memory);

        Assert.False(memory.TryRemember<ReturningStateMemory>(out ReturningStateMemory _));
        Assert.False(memory.TryRemember<WaitingForPotFateMemory>(out WaitingForPotFateMemory _));
    }

    // Discord (Rytlockie): wait a little before raising so nearby healers get the chance.
    [Theory]
    [InlineData(0, 0, TriageStage.Raising)]
    [InlineData(4, 5, TriageStage.Waiting)]
    [InlineData(5, 5, TriageStage.Raising)]
    [InlineData(94, 5, TriageStage.Raising)]
    [InlineData(95, 5, TriageStage.TimedOut)]
    public void TriageWaitsForItsDelayAndTheDelayExtendsTheTimeout(int elapsedSeconds, int delaySeconds, TriageStage expected)
    {
        Assert.Equal(
            expected,
            TriageRules.Stage(TimeSpan.FromSeconds(elapsedSeconds), TimeSpan.FromSeconds(delaySeconds), TimeSpan.FromSeconds(90)));
    }

    // GitHub #226: walked through mobs to a shard instead of using Return.
    [Fact]
    public void PreferringReturnRemovesTheWalkToAShardOnLongTrips()
    {
        Assert.Contains(RouteOptions.Calculators(considerReturn: true, preferReturn: false), c => c is WalkTeleportWalkCalculator);
        Assert.DoesNotContain(RouteOptions.Calculators(considerReturn: true, preferReturn: true), c => c is WalkTeleportWalkCalculator);
        Assert.Contains(RouteOptions.Calculators(considerReturn: true, preferReturn: true), c => c is ReturnTeleportWalkCalculator);
    }

    [Fact]
    public void ShortTripsKeepTheShardWalkAndNeverUseReturn()
    {
        List<IGraphCandidateCalculator> calculators = RouteOptions.Calculators(considerReturn: false, preferReturn: true);

        Assert.Contains(calculators, c => c is WalkTeleportWalkCalculator);
        Assert.DoesNotContain(calculators, c => c is ReturnTeleportWalkCalculator);
    }

    // Discord (Balder): with "Do FATEs" off, Illegal Mode waited at a CE while a pot was due.
    [Fact]
    public void APotFateIsStillWantedWithDoFatesOffWhenAPotSettingIsOn()
    {
        AutomatorConfig settings = new() { ShouldDoFates = false, ShouldFarmPotChests = true, ShouldPrepositionToPots = false };
        FateScorer scorer = Scorer(settings, potFateId: 2073);

        Assert.True(scorer.Score(LiveFate(2073)).Value > 0f);
        Assert.Equal(0f, scorer.Score(LiveFate(2081)).Value);

        settings.ShouldFarmPotChests = false;
        Assert.Equal(0f, scorer.Score(LiveFate(2073)).Value);
    }

    private static InCombatHandler CombatHandler(AutomatorMemory memory, Func<bool> inCombat) =>
        new(
            StandIn.For<IObjectTable>(o => o.Answers("LocalPlayer", StandIn.For<IPlayerCharacter>())),
            StandIn.For<ICondition>(c => c.Answers("get_Item", args => args[0] is ConditionFlag.InCombat && inCombat())),
            StandIn.For<IFateContext>(),
            StandIn.For<ICriticalEncounterContext>(),
            StandIn.For<IPathfinder>(),
            memory,
            null!,
            new QuietLog<InCombatHandler>());

    // Audit 127: a spot saved in South Horn was picked in North Horn.
    [Fact]
    public void MobFarmerSkipsSpotsSavedInTheOtherZone()
    {
        FarmSpot south = new() { Name = "south", Priority = 5, Territory = 1252 };
        FarmSpot north = new() { Name = "north", Priority = 1, Territory = 1346 };
        MobFarmerConfig config = new() { Spots = [south, north] };
        FarmerSpotSession session = new(
            config,
            StandIn.For<IPlayer>(),
            StandIn.For<IObjectTable>(),
            StandIn.For<IPartyList>(),
            StandIn.For<IClientState>(c => c.Answers("TerritoryType", 1346u)));

        session.Begin();

        Assert.Same(north, session.Current);
    }

    private static MobFarmerService Farmer(RecordingCombat combat, Func<FarmerPhase> phase)
    {
        IStateMachine<FarmerPhase> machine = StandIn.For<IStateMachine<FarmerPhase>>(m => m.Answers("State", _ => phase()));
        IPlayer player = StandIn.For<IPlayer>();
        return new MobFarmerService(
            StandIn.For<IMobScanner>(),
            () => machine,
            StandIn.For<IPathfinder>(),
            player,
            OccultCrescent(),
            StandIn.For<IChatGui>(),
            StandIn.For<ICondition>(),
            new UIConfig(),
            StandIn.For<ITranslator<MainWindow>>(),
            StandIn.For<IAutomationModeGuard>(),
            combat,
            new FarmerSpotSession(new MobFarmerConfig(), player, StandIn.For<IObjectTable>(), StandIn.For<IPartyList>(), StandIn.For<IClientState>()),
            new QuietLog<MobFarmerService>());
    }

    private static FateScorer Scorer(AutomatorConfig settings, uint potFateId) =>
        new(
            settings,
            new FatesConfig { MaxFateProgressPercent = 75 },
            new PotsConfig(),
            OccultCrescent(zone => zone.Answers("IsPotFate", args => Convert.ToUInt32(args[0]) == potFateId)),
            StandIn.For<IObjectTable>(o => o.Answers("LocalPlayer", StandIn.For<IPlayerCharacter>())));

    private static IZoneProvider OccultCrescent(Action<StandIn>? zoneSetup = null)
    {
        IZone zone = StandIn.For<IZone>(z =>
        {
            z.Answers("IsOccultCrescentZone", true);
            zoneSetup?.Invoke(z);
        });
        return StandIn.For<IZoneProvider>(p => p.Answers("GetZone", zone));
    }

    private static Fate LiveFate(ushort id) =>
        new(
            new FateId(id),
            StandIn.For<IFate>(f => f
                .Answers("FateId", id)
                .Answers("Name", new SeString())
                .Answers("Progress", (byte)10)
                .Answers("TimeRemaining", 600L)),
            StandIn.For<IDataRepository<Lumina.Excel.Sheets.Fate>>());

    private sealed class RecordingCombat : IFarmerCombatController
    {
        public List<string> Calls { get; } = [];

        public bool IsActive => true;

        public void Prepare() => Calls.Add("prepare");

        public void EnableFighting() => Calls.Add("enable");

        public void Disable() => Calls.Add("disable");

        public void Tick() => Calls.Add("tick");

        public void Teardown() => Calls.Add("teardown");
    }
}
