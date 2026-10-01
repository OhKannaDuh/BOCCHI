using BOCCHI.Automator.Services;
using BOCCHI.Common;
using BOCCHI.Common.Config;
using BOCCHI.Common.Data.Paths;
using BOCCHI.Common.Data.StateMemory;
using BOCCHI.Common.Data.Zones;
using BOCCHI.Common.Services;
using BOCCHI.Common.UI;
using Dalamud.Bindings.ImGui;
using Dalamud.Plugin.Services;
using Ocelot.Services.Translation;
using Ocelot.Windows;

namespace BOCCHI.Automator;

public class AutomatorRenderer
(
    Func<IAutomator> automatorFactory,
    IAutomatorMemory memory,
    UIConfig uiConfig,
    IPotCycleTracker potCycle,
    IZoneProvider zones,
    IDataManager data,
    ITranslator<MainWindow> translator
) : IDynamicRenderer
{
    private IAutomator? automator;

    private IAutomator Automator => automator ??= automatorFactory();

    public MainWindowSection Section => MainWindowSection.Automation;

    public void Render()
    {
        if (ImGui.Button(Automator.Enabled
            ? translator.T(".automation.automator.disable")
            : translator.T(".automation.automator.enable")))
        {
            Automator.Toggle();
        }

        AutomatorPathControls.Draw(Automator, zones, translator, showRefresh: Automator.Enabled);

        bool inOccult = zones.GetZone().IsOccultCrescentZone();
        if (!Automator.Enabled && !HasDetails() && !inOccult)
        {
            return;
        }

        ImGui.Spacing();

        ImGui.PushStyleColor(ImGuiCol.Text, BocchiUi.Header);
        bool detailsOpen = ImGui.CollapsingHeader(
            translator.T(".automation.automator.details"),
            HasDetails() ? ImGuiTreeNodeFlags.DefaultOpen : ImGuiTreeNodeFlags.None);
        ImGui.PopStyleColor();

        if (!detailsOpen)
        {
            return;
        }

        ImGui.Indent();

        PotTimerUi.Draw(potCycle, zones, data, translator);
        ImGui.Spacing();

        if (Automator.Enabled)
        {
            Automator.Render();
        }

        if (memory.TryRemember<GoalMemory>(out GoalMemory goalMemory))
        {
            BocchiUi.LabelledValue(
                translator.T(".status.goal"),
                GoalFormatHelper.Describe(goalMemory.Goal, translator, data));
        }

        if (memory.TryRemember<PotChestFarmMemory>(out PotChestFarmMemory potFarm))
        {
            BocchiUi.LabelledValue(
                translator.T(".automation.automator.pot_chest_farm"),
                GoalFormatHelper.FateName(data, potFarm.FateId.Value));
            BocchiUi.LabelledValue(
                translator.T(".automation.automator.chests_remaining"),
                $"{potFarm.RemainingChests}/{potFarm.TotalChests}");
            if (potFarm.TotalChests > 0)
            {
                float cleared = 1f - (potFarm.RemainingChests / (float)potFarm.TotalChests);
                BocchiUi.DrawPercentBar(
                    cleared,
                    Math.Min(220f, ImGui.GetContentRegionAvail().X),
                    $"{potFarm.RemainingChests}/{potFarm.TotalChests}");
            }
        }

        if (memory.TryRemember<GoalPathStepMemory>(out GoalPathStepMemory goalPathStepMemory))
        {
            int stepIndex = 1;
            foreach (IPathStep step in goalPathStepMemory.PathSteps)
            {
                BocchiUi.LabelledValue(
                    $"{translator.T(".status.current_step")} {stepIndex++}",
                    PathStepFormatHelper.Describe(step, translator, data));
            }
        }

        ImGui.Unindent();
    }

    public bool ShouldRender() => uiConfig.ShowAutomationSection;

    private bool HasDetails() =>
        Automator.Enabled
        || memory.TryRemember<GoalMemory>(out GoalMemory _)
        || memory.TryRemember<PotChestFarmMemory>(out PotChestFarmMemory _)
        || memory.TryRemember<GoalPathStepMemory>(out GoalPathStepMemory _);
}
