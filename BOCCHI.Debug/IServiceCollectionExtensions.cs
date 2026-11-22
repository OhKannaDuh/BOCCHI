using BOCCHI.Debug.Panels;
using BOCCHI.Debug.Services;
using Microsoft.Extensions.DependencyInjection;
using Ocelot.Windows;

namespace BOCCHI.Debug;

public static class IServiceCollectionExtensions
{
    public static void LoadDebugModule(this IServiceCollection services)
    {
        services.AddSingleton<IDebugPanel, FatesDebugPanel>();
        services.AddSingleton<IDebugPanel, CriticalEncountersDebugPanel>();
        services.AddSingleton<IDebugPanel, JobLevelsDebugPanel>();
        services.AddSingleton<IDebugPanel, OccultStateDebugPanel>();
        services.AddSingleton<IDebugPanel, AethernetDebugPanel>();

        services.AddSingleton<DebugWindow>();
        services.AddSingleton<IDebugWindow>(sp => sp.GetRequiredService<DebugWindow>());
        services.AddSingleton<IWindow>(sp => sp.GetRequiredService<DebugWindow>());

#if DEBUG
        // These two overwrite the plugin's treasure route data, so players must not reach them.
        services.AddSingleton<IDebugPanel, TreasureHuntPrecomputePanel>();
        services.AddSingleton<IDebugPanel, TreasureLocationsExportPanel>();

        // Dev-only overlays (CE/aetheryte radius rings). Never ship to players — left on in .21 by mistake.
        services.AddSingleton<DrawCEs>();

        services.AddSingleton<OpenWindows>();
#endif
    }
}
