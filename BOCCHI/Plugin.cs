using BOCCHI.Automator;
using BOCCHI.Automator.ChainRecipes;
using BOCCHI.Buff;
using BOCCHI.Common.Config;
using BOCCHI.Common.Config.Fields;
using BOCCHI.Common.Config.Renderers;
using BOCCHI.Common.Data.SupportJobs;
using BOCCHI.Common.Data.Zones;
using BOCCHI.Common.Data.Zones.Graph.Factory;
using BOCCHI.Common.Data.Zones.Implementations.NorthHorn;
using BOCCHI.Common.Data.Zones.Implementations.SouthHorn;
using BOCCHI.Common.Services;
using BOCCHI.Common.Steps;
using BOCCHI.Config;
using BOCCHI.Data;
using BOCCHI.MobFarmer;
using BOCCHI.Renderers;
using BOCCHI.Services;
using BOCCHI.Services.Repair;
using BOCCHI.Trackers;
using BOCCHI.Treasure;
using BOCCHI.UI;
using BOCCHI.World;
using Dalamud.Configuration;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Microsoft.Extensions.DependencyInjection;
using Ocelot;
using Ocelot.Chain.Services;
using Ocelot.Config;
using Ocelot.Config.Renderers;
using Ocelot.Config.Renderers.Enum;
using Ocelot.ECommons.Services;
using Ocelot.Rotation.Services;
using Ocelot.Pathfinding.Services;
using Ocelot.Pictomancy.Services;
using Ocelot.Services.WindowManager;
using Ocelot.UI.Services;
using Ocelot.Windows;
using System.Reflection;
using BOCCHI.Services.MOTD;
using Ocelot.Lifecycle;
#if DEBUG
using BOCCHI.Debug;
#endif

namespace BOCCHI;

public sealed class Plugin(IDalamudPluginInterface plugin, IPluginLog logger) : OcelotPlugin(plugin, logger)
{
    // Avoid CS9107: primary-ctor params are also passed to the base ctor.
    private readonly IPluginLog logger = logger;
    private readonly IDalamudPluginInterface plugin = plugin;

    public override string Name
    {
        get => "BOCCHI";
    }

    protected override void Bootstrap(IServiceCollection services)
    {
        BootstrapOcelotModules(services);
        BootstrapConfiguration(services, plugin, logger);

        services.AddSingleton<TranslationLoader>();
        services.AddSingleton<ReturnYesNoInitializer>();

        services.AddSingleton<IMainRenderer, MainRenderer>();
        services.AddSingleton<IConfigRenderer, ConfigRenderer>();
        services.AddSingleton<IAutomationModeGuard, AutomationModeGuard>();
        services.AddSingleton<OperationalStatusBar>();
        services.AddSingleton<OccultCrescentWindowAutoOpener>();
        services.AddSingleton<IMainWindowTitleBarContributor, IllegalModeTitleBarContributor>();
        services.AddSingleton<IMainWindowTitleBarContributor, KofiTitleBarContributor>();
        services.AddSingleton<IFieldRenderer<MobMultiSelectAttribute>, MobMultiSelectRenderer>();
        services.AddSingleton<IFieldRenderer<DisabledFateIdsAttribute>, DisabledFateIdsRenderer>();
        services.AddSingleton<IFieldRenderer<DisabledCriticalEncounterIdsAttribute>, DisabledCriticalEncounterIdsRenderer>();
        services.AddSingleton<IFieldRenderer<MountSelectAttribute>, MountSelectRenderer>();
        services.AddSingleton<IFieldRenderer<PluginDependencyStatusAttribute>, PluginDependencyStatusRenderer>();
        services.AddSingleton<IMp3SoundPlayer, Mp3SoundPlayer>();
        services.AddSingleton<IFieldRenderer<Mp3SoundSelectAttribute>, Mp3SoundSelectRenderer>();
        services.AddSingleton<UILanguageDisplay>();
        services.AddSingleton<NoOpFilter<UILanguage>>();

        services.AddSingleton<MessageOfTheDayService>();
        services.AddSingleton<IOnStart>(sp => sp.GetRequiredService<MessageOfTheDayService>());
        services.AddSingleton<IOnStop>(sp => sp.GetRequiredService<MessageOfTheDayService>());

        services.AddSingleton<ISupportJobFactory, SupportJobFactory>();
        services.AddSingleton<ISupportJobChanger, SupportJobChanger>();

        services.AddZones()
            .AddZone<SouthHorn>()
            .AddZone<NorthHorn>();

        services.AddSingleton<IGraphFactory, GraphFactory>();

        services.AddSingleton<IActivityNavigation, ActivityNavigation>();

        services.AddSingleton<UnmountStep>();
        services.AddSingleton<RepairStep>();
        services.AddSingleton<IRepairService, RepairService>();
        services.AddSingleton<TeleportToAethernetChain>();

        services.LoadTrackersModule();
        services.LoadWorldModule();
        services.LoadBuffModule();

        services.LoadAutomatorModule();
        services.LoadMobFarmerModule();
        services.LoadTreasureModule();

        services.AddBocchiCommands();

#if DEBUG
        services.LoadDebugModule();
#endif
    }

    private static void BootstrapOcelotModules(IServiceCollection services)
    {
        services.LoadECommons();
        services.LoadPictomancy();
        services.LoadPathfinding();
        services.LoadChain();
        services.LoadRotations();
        services.LoadUI();
    }

    private static void BootstrapConfiguration(IServiceCollection services, IDalamudPluginInterface plugin, IPluginLog logger)
    {
        ConfigMigrator migrator = new(plugin, logger);
        if (migrator.ShouldMigrate())
        {
            migrator.Migrate();
        }

        Configuration cfg = plugin.GetPluginConfig() as Configuration ?? new Configuration();
        EnsureAutoConfigInstances(cfg);
        EnsureConfigDefaults(cfg);

        if (cfg.AutomatorConfig.StopAfterReturn)
        {
            logger.Info(
                "Illegal Mode: Stop after return and teleport is ON — after Return/aetheryte BOCCHI mounts and pauses so you can walk the rest "
                + "(Illegal Mode → Stop after return and teleport). Toggle Illegal Mode to resume, or turn the option off.");
        }

        services.AddSingleton(cfg);
        services.AddSingleton<IConfiguration>(cfg);
        services.AddSingleton<IPluginConfiguration>(s => s.GetRequiredService<Configuration>());
        PropertyInfo[] properties = typeof(IConfiguration).GetProperties(BindingFlags.Instance | BindingFlags.Public);

        foreach(PropertyInfo property in properties)
        {
            PropertyInfo prop = property;
            Type propType = prop.PropertyType;

            services.AddSingleton(propType, sp =>
            {
                IConfiguration conf = sp.GetRequiredService<IConfiguration>();
                return prop.GetValue(conf)!;
            });

            if (typeof(IAutoConfig).IsAssignableFrom(propType))
            {
                services.AddSingleton(typeof(IAutoConfig), sp =>
                {
                    IConfiguration conf = sp.GetRequiredService<IConfiguration>();
                    return prop.GetValue(conf)!;
                });
            }
        }
    }

    /// <summary>
    ///     Dalamud/Newtonsoft can leave new IAutoConfig properties null when absent from saved JSON.
    ///     Null entries break ConfigRenderer and hide those pages.
    /// </summary>
    private static void EnsureAutoConfigInstances(Configuration cfg)
    {
        foreach (PropertyInfo prop in typeof(IConfiguration).GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (!typeof(IAutoConfig).IsAssignableFrom(prop.PropertyType) || prop.GetValue(cfg) is not null)
            {
                continue;
            }

            prop.SetValue(cfg, Activator.CreateInstance(prop.PropertyType));
        }
    }

    /// <summary>Null HashSets from bad/partial JSON would NRE in allowlist checks.</summary>
    private static void EnsureConfigDefaults(Configuration cfg)
    {
        cfg.FatesConfig.DisabledFateIds ??= [];
        cfg.CriticalEncountersConfig.DisabledCriticalEncounterIds ??= [];
        cfg.ShoppingConfig.PreferredItemIds ??= [];
        cfg.MobFarmerConfig.Mobs ??= [];
    }
}
