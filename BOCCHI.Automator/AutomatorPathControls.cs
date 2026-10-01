using BOCCHI.Automator.Services;
using BOCCHI.Common.Data.Zones;
using Dalamud.Bindings.ImGui;
using Ocelot.Services.Translation;
using Ocelot.Windows;

namespace BOCCHI.Automator;

internal static class AutomatorPathControls
{
    public static void Draw(
        IAutomator automator,
        IZoneProvider zones,
        ITranslator<MainWindow> translator,
        bool showRefresh)
    {
        if (showRefresh)
        {
            ImGui.SameLine();
            if (ImGui.Button(translator.T(".automation.automator.refresh_pathfinding")))
            {
                automator.RefreshPathfinding();
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(translator.T(".automation.automator.refresh_pathfinding_tooltip"));
            }
        }

        if (!zones.GetZone().IsOccultCrescentZone())
        {
            return;
        }

        ImGui.SameLine();
        if (ImGui.Button(translator.T(".automation.automator.rebuild_path_map")))
        {
            automator.RebuildPathMap();
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(translator.T(".automation.automator.rebuild_path_map_tooltip"));
        }
    }
}
