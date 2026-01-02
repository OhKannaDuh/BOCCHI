using BOCCHI.Automator.Data;
using BOCCHI.Automator.Data.StateMemory;
using BOCCHI.Automator.Services;
using BOCCHI.Common;
using BOCCHI.Common.Data.StateMemory;
using BOCCHI.Common.Services;
using Dalamud.Bindings.ImGui;
using Ocelot.Services.Pathfinding;
using Ocelot.Services.UI;

namespace BOCCHI.Automator;

public class AutomatorRenderer(
    IAutomator automator,
    IAutomatorMemory memory,
    IPathfinder pathfinder,
    IUIService ui
) : IDynamicRenderer
{
    public void Render()
    {
        ui.LabelledValue("Automator State", automator.Enabled);

        if (ImGui.Button("Toggle Automator"))
        {
            automator.Toggle();
            if (!automator.Enabled)
            {
                memory.Wipe();
                pathfinder.Stop();
            }
        }

        if (automator.Enabled)
        {
            automator.Render();
        }

        if (memory.TryRemember<GoalMemory>(out var goalMemory))
        {
            ui.LabelledValue("Goal", goalMemory.Goal.Describe());
        }

        if (memory.TryRemember<GoalPathStepMemory>(out var goalPathStepMemory))
        {
            foreach (var step in goalPathStepMemory.PathSteps)
            {
                ui.LabelledValue("Step", step.Describe());
            }
        }
    }

    public bool ShouldRender()
    {
        return true;
    }
}
