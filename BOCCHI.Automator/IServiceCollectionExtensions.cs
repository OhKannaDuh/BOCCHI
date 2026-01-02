using BOCCHI.Automator.Data;
using BOCCHI.Automator.Data.Goals;
using BOCCHI.Automator.Services;
using BOCCHI.Automator.Services.Goals;
using BOCCHI.Automator.Services.Paths;
using BOCCHI.Common;
using BOCCHI.Common.Services;
using BOCCHI.Common.Services.Paths;
using Microsoft.Extensions.DependencyInjection;
using Ocelot;
using Ocelot.States;

namespace BOCCHI.Automator;

public static class IServiceCollectionExtensions
{
    public static void LoadAutomatorModule(this IServiceCollection services)
    {
        Registry.RegisterAssemblies(typeof(AutomatorState).Assembly);

        services.AddSingleton<IAutomator, Services.Automator>();
        services.AddSingleton<IAutomatorContext, AutomatorContext>();
        services.AddSingleton<IDynamicRenderer, AutomatorRenderer>();

        services.AddSingleton<IGoalFactory, GoalFactory>();
        services.AddSingleton<IGoalValidator, GoalValidator>();

        services.AddSingleton<IPathCalculator, PathCalculator>();
        services.AddSingleton<IPathStepExecutor, PathStepExecutor>();

        services.AddSingleton<IAutomatorMemory, AutomatorMemory>();

        services.AddScoreStateMachine<AutomatorState, StatePriority>(AutomatorState.Entry);
    }
}
