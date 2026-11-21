using BOCCHI.Common.Services;
using Microsoft.Extensions.DependencyInjection;

namespace BOCCHI.Common.Data.Zones;

public static class ZoneServiceCollectionExtensions
{
    public static IServiceCollection AddZones(this IServiceCollection services)
    {
        services.AddSingleton<IZoneProvider, ZoneProvider>();
        services.AddSingleton<IPotCycleTracker, PotCycleTracker>();
        services.AddSingleton<PotCycleSyncService>();
        services.AddSingleton<StuckJumpAssist>();
        services.AddSingleton<YesAlreadyPause>();
        services.AddSingleton<AutoRevive>();
        return services;
    }

    public static IServiceCollection AddZone<TZone>(this IServiceCollection services)
    where TZone : class, IZone
    {
        services.AddSingleton<TZone>();
        services.AddSingleton<IZone>(sp => sp.GetRequiredService<TZone>());
        return services;
    }
}
