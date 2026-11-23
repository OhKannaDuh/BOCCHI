using System.Text.Json;
using Ocelot.Ipc.BossMod;
using Ocelot.Rotation.Services;
using Ocelot.Rotation.Services.BossMod;
using Ocelot.Services.PlayerState;
using Xunit;

namespace BOCCHI.Tests;

public class BossModPresetEngineTests
{
    // Audit: refused preset writes retry after five seconds instead of every frame.
    [Fact]
    public void RefusedPresetWritesWaitFiveSecondsBeforeRetrying()
    {
        DateTime now = new(2026, 1, 1);
        int writes = 0;
        IBossModIpc ipc = StandIn.For<IBossModIpc>(s => s
            .Answers("IsAvailable", true)
            .Answers("Create", _ => { writes++; return false; }));
        BossModPresetEngine engine = new(ipc, StandIn.For<IPlayer>(), new(), () => now);
        engine.Enable(CombatActivity.Fate);
        Assert.Equal(3, writes);
        now = now.AddMilliseconds(4999);
        engine.Refresh();
        Assert.Equal(3, writes);
        now = now.AddMilliseconds(1);
        engine.Refresh();
        Assert.Equal(6, writes);
    }

    // Audit: re-enabling an armed activity does not poll IPC every frame.
    [Fact]
    public void ArmedActivitySkipsIpcUntilOneSecondHasElapsed()
    {
        PresetFixture fixture = new();
        fixture.Engine.Enable(CombatActivity.Fate);
        fixture.Calls.Clear();
        fixture.Now = fixture.Now.AddMilliseconds(999);
        fixture.Engine.Enable(CombatActivity.Fate);
        Assert.Empty(fixture.Calls);
        fixture.Now = fixture.Now.AddMilliseconds(1);
        fixture.Engine.Enable(CombatActivity.Fate);
        Assert.Contains("GetActive", fixture.Calls);
        Assert.DoesNotContain("Activate", fixture.Calls);
    }

    // Audit: recreating presets during a session preserves its selected rotation kind.
    [Fact]
    public void ForceRecreatePreservesActiveSessionPresetKind()
    {
        PresetFixture fixture = new();
        fixture.Engine.EnsurePresets(BossModPresetKind.FullAr);
        fixture.Engine.Enable(CombatActivity.CriticalEncounter);
        fixture.Created.Clear();
        Assert.True(fixture.Engine.TryForceRecreate(BossModPresetKind.MiscAi));
        Assert.Equal(new[] { fixture.Naming.FateFullAr, fixture.Naming.CeFullAr, fixture.Naming.MobFarmFullAr }, fixture.Created);
        Assert.Equal(fixture.Naming.CeFullAr, fixture.Active);
    }

    // Audit: BossMod reloads require movement strategies to be applied again.
    [Fact]
    public void AvailabilityRecoveryReappliesMovementStrategies()
    {
        PresetFixture fixture = new();
        fixture.Engine.Enable(CombatActivity.Fate);
        Assert.Equal(5, fixture.Strategies);
        fixture.Engine.Refresh();
        Assert.Equal(5, fixture.Strategies);
        fixture.Available = false;
        fixture.Engine.Refresh();
        fixture.Available = true;
        fixture.Engine.Refresh();
        Assert.Equal(10, fixture.Strategies);
    }

    private sealed class PresetFixture
    {
        public DateTime Now = new(2026, 1, 1);
        public bool Available = true;
        public string? Active;
        public int Strategies;
        public readonly CombatAiPresetNaming Naming = new();
        public readonly List<string> Created = [];
        public readonly List<string> Calls;
        public readonly BossModPresetEngine Engine;
        private readonly Dictionary<string, string> presets = [];

        public PresetFixture()
        {
            IBossModIpc ipc = StandIn.For<IBossModIpc>(s => s
                .Answers("IsAvailable", _ => Available)
                .Answers("Get", args => presets.GetValueOrDefault((string)args[0]!))
                .Answers("Create", args =>
                {
                    string json = (string)args[0]!;
                    using JsonDocument document = JsonDocument.Parse(json);
                    string name = document.RootElement.GetProperty("Name").GetString()!;
                    presets[name] = json;
                    Created.Add(name);
                    return true;
                })
                .Answers("Delete", args => presets.Remove((string)args[0]!))
                .Answers("Activate", args => { Active = (string)args[0]!; return true; })
                .Answers("GetActive", _ => Active)
                .Answers("AddTransientStrategy", _ => { Strategies++; return true; }));
            Calls = ((StandIn)(object)ipc).Calls;
            Engine = new(ipc, StandIn.For<IPlayer>(), Naming, () => Now);
        }
    }
}
