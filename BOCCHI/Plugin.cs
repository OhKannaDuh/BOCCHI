using BOCCHI.Automator;
using BOCCHI.Buff;
using BOCCHI.Common.Config;
using BOCCHI.Common.Config.Fields;
using BOCCHI.Common.Config.Renderers;
using BOCCHI.Common.Data.Aethernet;
using BOCCHI.Common.Data.SupportJobs;
using BOCCHI.Common.Data.Zones;
using BOCCHI.Common.Data.Zones.Graph.Factory;
using BOCCHI.Common.Data.Zones.Implementations.NorthHorn;
using BOCCHI.Common.Data.Zones.Implementations.SouthHorn;
using BOCCHI.Common.Ipc.Knightshopper;
using BOCCHI.Common.Services;
using BOCCHI.Common.Steps;
using BOCCHI.Config;
using BOCCHI.Data;
using BOCCHI.MobFarmer;
using BOCCHI.Renderers;
using BOCCHI.Services;
using BOCCHI.Services.Changelog;
using BOCCHI.Services.Repair;
using BOCCHI.Services.Shopping;
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
using BOCCHI.Services.Logging;
using BOCCHI.Common.Services.Logging;
using BOCCHI.Debug;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Ocelot.Lifecycle;

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
        services.AddSingleton<OccultExcelInitializer>();

        services.AddSingleton<IMainRenderer, MainRenderer>();
        services.AddSingleton<IConfigRenderer, ConfigRenderer>();
        services.AddSingleton<IAutomationModeGuard, AutomationModeGuard>();
        services.AddSingleton<OperationalStatusBar>();
        services.AddSingleton<OccultCrescentWindowAutoOpener>();
        services.AddSingleton<CombatPathfindCancelService>();
        services.AddSingleton<IMainWindowTitleBarContributor, IllegalModeTitleBarContributor>();
        services.AddSingleton<IMainWindowTitleBarContributor, LogsTitleBarContributor>();
        services.AddSingleton<IMainWindowTitleBarContributor, KofiTitleBarContributor>();
        services.AddSingleton<IFieldRenderer<MobMultiSelectAttribute>, MobMultiSelectRenderer>();
        services.AddSingleton<IFieldRenderer<DisabledFateIdsAttribute>, DisabledFateIdsRenderer>();
        services.AddSingleton<IFieldRenderer<DisabledCriticalEncounterIdsAttribute>, DisabledCriticalEncounterIdsRenderer>();
        services.AddSingleton<IFieldRenderer<MountSelectAttribute>, MountSelectRenderer>();
        services.AddSingleton<IFieldRenderer<PluginDependencyStatusAttribute>, PluginDependencyStatusRenderer>();
        services.AddSingleton<IFieldRenderer<LogsViewerAttribute>, LogsViewerRenderer>();
        services.AddSingleton<IMp3SoundPlayer, Mp3SoundPlayer>();
        services.AddSingleton<IFieldRenderer<Mp3SoundSelectAttribute>, Mp3SoundSelectRenderer>();
        services.AddSingleton<UILanguageDisplay>();
        services.AddSingleton<NoOpFilter<UILanguage>>();
        services.AddSingleton<CombatAutorotationDisplay>();
        services.AddSingleton<CombatAutorotationFilter>();
        services.AddSingleton<AutoRepairMethodDisplay>();
        services.AddSingleton<NoOpFilter<AutoRepairMethod>>();
        services.AddSingleton<IFieldRenderer<TriageRaiseJobAttribute>, TriageRaiseJobRenderer>();
        services.AddSingleton<IFieldRenderer<BossModPresetOptionsAttribute>, BossModPresetOptionsRenderer>();
        services.AddSingleton<IFieldRenderer<FarmSpotListAttribute>, FarmSpotListRenderer>();
        services.AddSingleton<MobFarmerYieldService>();

        services.AddSingleton<MessageOfTheDayService>();
        services.AddSingleton<IOnStart>(sp => sp.GetRequiredService<MessageOfTheDayService>());
        services.AddSingleton<IOnStop>(sp => sp.GetRequiredService<MessageOfTheDayService>());

        services.AddSingleton<ChangelogWindow>();
        services.AddSingleton<IChangelogWindow>(sp => sp.GetRequiredService<ChangelogWindow>());
        services.AddSingleton<IWindow>(sp => sp.GetRequiredService<ChangelogWindow>());
        services.AddSingleton<ChangelogPopupService>();

        services.AddSingleton<IBocchiLogBuffer, BocchiLogBuffer>();
        services.AddSingleton<IBocchiLogDiagnostics, BocchiLogDiagnostics>();
        services.AddSingleton<IBocchiLogClipboard, BocchiLogClipboard>();
        services.AddSingleton<BocchiLogsPanel>();
        services.AddSingleton<LogsWindow>();
        services.AddSingleton<ILogsWindow>(sp => sp.GetRequiredService<LogsWindow>());
        services.AddSingleton<IWindow>(sp => sp.GetRequiredService<LogsWindow>());
        services.RemoveAll<IPluginLog>();
        services.AddSingleton<IPluginLog>(sp =>
        {
            DalamudServices bag = sp.GetRequiredService<DalamudServices>();
            return new CapturingPluginLog(bag.PluginLog, sp.GetRequiredService<IBocchiLogBuffer>());
        });

        services.AddSingleton<ISupportJobFactory, SupportJobFactory>();
        services.AddSingleton<ISupportJobChanger, SupportJobChanger>();

        services.AddZones()
            .AddZone<SouthHorn>()
            .AddZone<NorthHorn>();

        services.AddSingleton<IGraphFactory, GraphFactory>();

        services.AddSingleton<CriticalEncounterGeometry>();
        services.AddSingleton<IActivityNavigation, ActivityNavigation>();
        services.AddSingleton<IFieldNoteTracker, FieldNoteTracker>();

        services.AddSingleton<UnmountStep>();
        services.AddSingleton<RepairStep>();
        services.AddSingleton<NpcRepairStep>();
        services.AddSingleton<IRepairService, RepairService>();
        services.AddSingleton<AethernetTeleportChain>();
        services.AddSingleton<IKnightshopperIpc, KnightshopperIpc>();
        services.AddSingleton<ShoppingService>();
        services.AddSingleton<IShoppingService>(sp => sp.GetRequiredService<ShoppingService>());
        services.AddSingleton<Func<IShoppingService>>(sp => () => sp.GetRequiredService<IShoppingService>());
        services.LoadTrackersModule();
        services.LoadWorldModule();
        services.LoadBuffModule();

        services.LoadAutomatorModule();
        services.LoadMobFarmerModule();
        services.LoadTreasureModule();

        services.AddBocchiCommands();
        services.LoadDebugModule();
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
        SanitizeCombatAutorotation(cfg.AutomatorConfig, plugin, logger);

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
        cfg.MobFarmerConfig.Mobs ??= [];
        SanitizeAutomatorConfig(cfg.AutomatorConfig);
        SanitizeTreasureConfig(cfg.TreasureConfig);
        SanitizeBuffConfig(cfg.BuffConfig);
        SanitizeShoppingConfig(cfg.ShoppingConfig);
    }

    /// <summary>
    ///     Drop a combat backend whose plugin has been uninstalled. Keyed on
    ///     <see cref="IDalamudPluginInterface.InstalledPlugins"/> rather than "is it loaded": load
    ///     order is not guaranteed, so a plugin that has not initialised yet would look absent and
    ///     silently reset a valid setting.
    /// </summary>
    private static void SanitizeCombatAutorotation(
        AutomatorConfig automator,
        IDalamudPluginInterface plugin,
        IPluginLog logger)
    {
        string? required = automator.CombatAutorotation switch
        {
            CombatAutorotation.WrathCombo => "WrathCombo",
            CombatAutorotation.RotationSolverReborn => CombatPluginPresence.RotationSolver,
            CombatAutorotation.BossMod => "BossMod",
            CombatAutorotation.BossModReborn => "BossModReborn",
            _ => null,
        };

        if (required == null || plugin.InstalledPlugins.Any(p => p.InternalName == required))
        {
            return;
        }

        logger.Info(
            "Illegal Mode combat was set to {Backend}, but {Plugin} is not installed — falling back to None.",
            automator.CombatAutorotation,
            required);
        automator.CombatAutorotation = CombatAutorotation.None;
    }

    /// <summary>
    ///     UI ranges are not enforced on load — early/partial JSON can leave delays that look like stuck pathing.
    /// </summary>
    private static void SanitizeAutomatorConfig(AutomatorConfig automator)
    {
        automator.MaxRemoteIdleTimeSeconds = Math.Clamp(automator.MaxRemoteIdleTimeSeconds, 2, 60);
        automator.MaxBaseTeleportDelaySeconds = Math.Clamp(automator.MaxBaseTeleportDelaySeconds, 0, 60);
        automator.TreasureSightRecastIntervalSeconds =
            Math.Clamp(automator.TreasureSightRecastIntervalSeconds, 60, 600);
        automator.LeaveFateTravelForCeSeconds = Math.Clamp(automator.LeaveFateTravelForCeSeconds, 0, 180);
        automator.AutoRepairThreshold = Math.Clamp(automator.AutoRepairThreshold, 1, 99);
    }

    private static void SanitizeTreasureConfig(TreasureConfig treasure)
    {
        treasure.TreasureSightEveryNLocations = Math.Clamp(treasure.TreasureSightEveryNLocations, 1, 50);
        treasure.HuntMaxLevel = Math.Clamp(treasure.HuntMaxLevel, 1, 50);
        treasure.HuntMinBronzePercent = Math.Clamp(treasure.HuntMinBronzePercent, 0, 100);
        treasure.HuntMinSilverPercent = Math.Clamp(treasure.HuntMinSilverPercent, 0, 100);
        treasure.EmptyPadTrustDistance = Math.Clamp(treasure.EmptyPadTrustDistance, 10f, 60f);
    }

    private static void SanitizeBuffConfig(BuffConfig buff)
    {
        buff.ReapplyThreshold = Math.Clamp(buff.ReapplyThreshold, 0, 25);
    }

    private static void SanitizeShoppingConfig(ShoppingConfig shopping)
    {
        shopping.SilverThreshold = Math.Clamp(shopping.SilverThreshold, 0, 9999);
        shopping.GoldThreshold = Math.Clamp(shopping.GoldThreshold, 0, 9999);
    }

}
