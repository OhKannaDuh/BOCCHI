using System.Linq;
using BOCCHI.Enums;
using Dalamud.Game.ClientState.Objects.SubKinds;
using ECommons.DalamudServices;
using ECommons.Throttlers;
using Ocelot.Modules;

namespace BOCCHI.Modules.Data;

// [OcelotModule(int.MaxValue)]
public class DataModule : Module
{
    public override DataConfig Config
    {
        get => PluginConfig.DataConfig;
    }

    public override bool ShouldInitialize
    {
        get => true;
    }

    private readonly Api api;

    public DataModule(Plugin plugin, Config config)
        : base(plugin, config)
    {
        api = new Api(this);
    }

    public override void PostInitialize()
    {
        api.Initialize();
    }

    public override void Update(UpdateContext context)
    {
        if (!Config.Enabled)
        {
            return;
        }

        if (!EzThrottler.Throttle("ApiScan", 2500))
        {
            return;
        }

        var enemies = TargetHelper.Enemies.Where(e => e.Name.TextValue.Length > 0);
        foreach (var enemy in enemies)
        {
            api.SendEnemyData(enemy);
        }

        var traps = Svc.Objects.OfType<IEventObj>()
            .Where(o => o.BaseId is (uint)OccultObjectType.Trap or (uint)OccultObjectType.BigTrap);
        foreach (var trap in traps)
        {
            api.SendTrapData(trap);
        }
    }
}
