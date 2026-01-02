using BOCCHI.Common;
using BOCCHI.Common.Services;
using BOCCHI.CriticalEncounters.Data;
using BOCCHI.CriticalEncounters.Services;
using Microsoft.Extensions.DependencyInjection;

namespace BOCCHI.CriticalEncounters;

public static class IServiceCollectionExtensions
{
    public static void LoadCriticalEncountersModule(this IServiceCollection services)
    {
        services.AddSingleton<IDynamicRenderer, CriticalEncountersRenderer>();
        services.AddSingleton<ICriticalEncounterRepository, CriticalEncounterRepository>();
        services.AddSingleton<ICriticalEncounterFactory, CriticalEncounterFactory>();
        services.AddSingleton<ICriticalEncounterContext, CriticalEncounterContext>();
    }
}
