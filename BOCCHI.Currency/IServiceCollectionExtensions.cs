using BOCCHI.Common;
using BOCCHI.Currency.Services;
using Microsoft.Extensions.DependencyInjection;

namespace BOCCHI.Currency;

public static class IServiceCollectionExtensions
{
    public static void LoadCurrencyModule(this IServiceCollection services)
    {
        services.AddSingleton<ICurrencyTracker, CurrencyTracker>();
        services.AddSingleton<IDynamicRenderer, CurrencyRenderer>();
    }
}
