using BOCCHI.Automator.Data;
using BOCCHI.Automator.Data.StateMemory;
using BOCCHI.Automator.Services.Goals;
using BOCCHI.Automator.Services.Paths;
using BOCCHI.Common.Data.CriticalEncounters;
using BOCCHI.Common.Data.Fates;
using BOCCHI.Common.Data.StateMemory;
using BOCCHI.Common.Services;
using BOCCHI.Common.Services.Paths;
using FFXIVClientStructs.FFXIV.Client.Game.Fate;
using Ocelot.Chain;
using Ocelot.Lifecycle;
using Ocelot.Services.Logger;
using Ocelot.States;

namespace BOCCHI.Automator.Services;

public class Automator(
    IAutomatorMemory memory,
    IStateMachine<AutomatorState> stateMachine,
    IPathCalculator calculator,
    IGoalValidator validator,
    IAutomatorContext context,
    IChainManager manager,
    ILogger<Automator> logger
) : IAutomator, IOnUpdate
{
    public bool Enabled
    {
        get => context.Enabled;
    }

    public void Toggle()
    {
        context.Toggle();
    }

    public void Render()
    {
        stateMachine.Render();
    }

    public void Update()
    {
        if (!Enabled)
        {
            memory.Wipe();
            manager.CancelAll();
            return;
        }

        if (memory.TryRemember<GoalMemory>(out var goal))
        {
            if (!validator.Validate(goal.Goal))
            {
                memory.Forget<GoalMemory>();
                memory.Forget<GoalPathStepMemory>();
            }
            else if (!memory.TryRemember<GoalPathStepMemory>(out var _))
            {
                memory.TryAdd(new GoalPathStepMemory(goal.Goal, calculator));
            }
        }

        stateMachine.Update();
    }
}
