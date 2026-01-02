using BOCCHI.Common;
using BOCCHI.Experience.Services;
using Microsoft.Extensions.DependencyInjection;

namespace BOCCHI.Experience;

public static class IServiceCollectionExtensions
{
    public static void LoadExperienceModule(this IServiceCollection services)
    {
        services.AddSingleton<IExperienceTracker, ExperienceTracker>();
        services.AddSingleton<IDynamicRenderer, ExperienceRenderer>();
    }
}
