using BOCCHI.Common;
using BOCCHI.Common.Services;
using BOCCHI.Fates.Data;
using BOCCHI.Fates.Services;
using Microsoft.Extensions.DependencyInjection;

namespace BOCCHI.Fates;

public static class IServiceCollectionExtensions
{
    public static void LoadFatesModule(this IServiceCollection services)
    {
        services.AddSingleton<IDynamicRenderer, FatesRenderer>();
        services.AddSingleton<IFateRepository, FateRepository>();
        services.AddSingleton<IFateFactory, FateFactory>();
        services.AddSingleton<IFateContext, FateContext>();
    }
}
