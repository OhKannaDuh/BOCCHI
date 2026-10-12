using BOCCHI.Automator.Data;
using BOCCHI.Automator.Data.Goals;
using BOCCHI.Automator.Services;
using BOCCHI.Automator.Services.Goals;
using BOCCHI.Automator.Services.Paths;
using BOCCHI.Automator.Services.PotTreasure;
using BOCCHI.Common;
using BOCCHI.Common.Data.Goals;
using BOCCHI.Common.Services;
using BOCCHI.Common.Services.Paths;
using Microsoft.Extensions.DependencyInjection;
using Ocelot;
using Ocelot.Lifecycle;
using Ocelot.States;

namespace BOCCHI.Automator;

public static class IServiceCollectionExtensions
{
    public static void LoadAutomatorModule(this IServiceCollection services)
    {
        Registry.RegisterAssemblies(typeof(AutomatorState).Assembly);

        services.AddSingleton<IAutomator, Services.Automator>();
        services.AddSingleton<Func<IAutomator>>(sp => () => sp.GetRequiredService<IAutomator>());
        services.AddSingleton<IAutomatorContext, AutomatorContext>();
        services.AddSingleton<IPotsTreasureMode, PotsTreasureService>();
        services.AddSingleton<Func<IPotsTreasureMode>>(sp => () => sp.GetRequiredService<IPotsTreasureMode>());
        services.AddSingleton<IllegalModeTreasureFillerService>();
        services.AddSingleton<IIllegalModeStartableActivityProbe, IllegalModeStartableActivityProbe>();
        services.AddSingleton<TriageLatchService>();
        services.AddSingleton<PotTreasureHintTracker>();
        services.AddSingleton<IOnStart>(sp => sp.GetRequiredService<PotTreasureHintTracker>());
        services.AddSingleton<IOnStop>(sp => sp.GetRequiredService<PotTreasureHintTracker>());
        services.AddSingleton<IDynamicRenderer, AutomatorRenderer>();
        services.AddSingleton<IDynamicRenderer, CompletionistRenderer>();
        services.AddSingleton<IDynamicRenderer, PotsTreasureRenderer>();

        services.AddSingleton<IGoalFactory, GoalFactory>();
        services.AddSingleton<IGoalValidator, GoalValidator>();
        services.AddSingleton<IStartableCriticalEncounterFinder, StartableCriticalEncounterFinder>();
        services.AddSingleton<IForkedTowerRegistration, ForkedTowerRegistration>();
        services.AddSingleton<AutoRotationController>();

        services.AddSingleton<IPathCalculator, PathCalculator>();
        services.AddSingleton<IPathStepExecutor, PathStepExecutor>();

        services.AddSingleton<IAutomatorMemory, AutomatorMemory>();

        services.AddScoreStateMachine<AutomatorState, StatePriority>(AutomatorState.Entry);
        services.AddSingleton<Func<IStateMachine<AutomatorState>>>(sp => () => sp.GetRequiredService<IStateMachine<AutomatorState>>());
    }
}
