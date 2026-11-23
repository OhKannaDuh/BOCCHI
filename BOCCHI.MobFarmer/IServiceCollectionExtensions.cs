using BOCCHI.Common;
using BOCCHI.MobFarmer.Data;
using BOCCHI.MobFarmer.Services;
using Microsoft.Extensions.DependencyInjection;
using Ocelot;
using Ocelot.States;

namespace BOCCHI.MobFarmer;

public static class IServiceCollectionExtensions
{
    public static void LoadMobFarmerModule(this IServiceCollection services)
    {
        Registry.RegisterAssemblies(typeof(FarmerPhase).Assembly);

        services.AddSingleton<MobFarmerPanelState>();
        services.AddSingleton<IMobScanner, MobScanner>();
        services.AddSingleton<IFarmerCombatController, FarmerCombatController>();
        services.AddSingleton<FarmerPullAssist>();
        services.AddSingleton<FarmerSpotSession>();
        services.AddSingleton<IMobFarmer, MobFarmerService>();
        services.AddSingleton<Func<IMobFarmer>>(sp => () => sp.GetRequiredService<IMobFarmer>());
        services.AddSingleton<IDynamicRenderer, MobFarmerRenderer>();

        services.AddFlowStateMachine(FarmerPhase.Waiting);
        services.AddSingleton<Func<IStateMachine<FarmerPhase>>>(sp => () => sp.GetRequiredService<IStateMachine<FarmerPhase>>());
    }
}
