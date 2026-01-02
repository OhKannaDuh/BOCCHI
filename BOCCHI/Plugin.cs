using System.Reflection;
using BOCCHI.Automator;
using BOCCHI.Automator.ChainRecipes;
using BOCCHI.Buff;
using BOCCHI.Common.Config;
using BOCCHI.Common.Data.SupportJobs;
using BOCCHI.Common.Data.Zones;
using BOCCHI.Common.Services;
using BOCCHI.Config;
using BOCCHI.CriticalEncounters;
using BOCCHI.Currency;
using BOCCHI.Data;
using BOCCHI.Data.Zones;
using BOCCHI.Experience;
using BOCCHI.Fates;
using BOCCHI.Renderers;
using BOCCHI.Services;
using Dalamud.Configuration;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Microsoft.Extensions.DependencyInjection;
using Ocelot;
using Ocelot.Config;
using Ocelot.Pathfinding.Services;
using Ocelot.Services.WindowManager;
using Ocelot.UI.Services;
using Ocelot.Chain.Services;
using Ocelot.ECommons.Services;
// using Ocelot.Mechanic.Services;
using Ocelot.Pictomancy.Services;
// using Ocelot.Rotation.Services;

namespace BOCCHI;

public sealed class Plugin(IDalamudPluginInterface plugin, IPluginLog logger) : OcelotPlugin(plugin, logger)
{
    private readonly IDalamudPluginInterface plugin = plugin;

    public override string Name { get; } = "BOCCHI";

    protected override void Boostrap(IServiceCollection services)
    {
        BootstrapOcelotModules(services);
        BootstrapConfiguration(services, plugin, logger);

        // services.AddSingleton<IEventHost, OnEnterOccultCrescentZoneHost>();
        // Why this crash :/
        // services.AddSingleton<IEventHost, OnAutomatorToggledHost>();

        services.AddSingleton<TranslationLoader>();

        services.AddSingleton<IMainRenderer, MainRenderer>();
        services.AddSingleton<IConfigRenderer, ConfigRenderer>();

        services.AddSingleton<ISupportJobFactory, SupportJobFactory>();
        services.AddSingleton<ISupportJobChanger, SupportJobChanger>();

        services.AddSingleton<IZoneProvider, ZoneProvider>();

        services.AddSingleton<TeleportToAethernetChain>();


#if DEBUG
        services.AddSingleton<OpenWindows>();
#endif

        services.LoadExperienceModule();
        services.LoadCurrencyModule();
        services.LoadBuffModule();
        services.LoadFatesModule();
        services.LoadCriticalEncountersModule();

        services.LoadAutomatorModule();
    }

    private static void BootstrapOcelotModules(IServiceCollection services)
    {
        services.LoadECommons();
        services.LoadPictomancy();
        services.LoadPathfinding();
        // services.LoadMechanics();
        // services.LoadRotations();
        services.LoadChain();
        services.LoadUI();
    }

    private static void BootstrapConfiguration(IServiceCollection services, IDalamudPluginInterface plugin, IPluginLog logger)
    {
        var migrator = new ConfigMigrator(plugin, logger);
        if (migrator.ShouldMigrate())
        {
            migrator.Migrate();
        }

        var cfg = plugin.GetPluginConfig() as Configuration ?? new Configuration();
        services.AddSingleton(cfg);
        services.AddSingleton<IConfiguration>(cfg);
        services.AddSingleton<IPluginConfiguration>(s => s.GetRequiredService<Configuration>());
        var properties = typeof(IConfiguration).GetProperties(BindingFlags.Instance | BindingFlags.Public);
        foreach (var property in properties)
        {
            var prop = property;
            var propType = prop.PropertyType;

            services.AddSingleton(propType, sp =>
            {
                var conf = sp.GetRequiredService<IConfiguration>();
                return prop.GetValue(conf)!;
            });

            if (typeof(IAutoConfig).IsAssignableFrom(propType))
            {
                services.AddSingleton(typeof(IAutoConfig), sp =>
                {
                    var conf = sp.GetRequiredService<IConfiguration>();
                    return prop.GetValue(conf)!;
                });
            }
        }
    }
}
