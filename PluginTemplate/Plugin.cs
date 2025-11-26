using Dalamud.Plugin;
using Microsoft.Extensions.DependencyInjection;
using Ocelot;
// using Ocelot.Chain.Services;
// using Ocelot.ECommons.Services;
// using Ocelot.Mechanic.Services;
// using Ocelot.Pathfinding.Services;
// using Ocelot.Pictomancy.Services;
// using Ocelot.Rotation.Services;
// using Ocelot.UI.Services;

namespace PluginTemplate;

public sealed class Plugin(IDalamudPluginInterface plugin) : OcelotPlugin(plugin)
{
    private readonly IDalamudPluginInterface plugin = plugin;

    protected override void Boostrap(IServiceCollection services)
    {
        BootstrapOcelotModules(services);
    }

    private static void BootstrapOcelotModules(IServiceCollection services)
    {
        // services.LoadECommons();
        // services.LoadPictomancy();
        // services.LoadPathfinding();
        // services.LoadMechanics();
        // services.LoadRotations();
        // services.LoadChain();
        // services.LoadUI();
    }
}
