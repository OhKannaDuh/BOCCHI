using BOCCHI.Common.Data.Mobs;
using BOCCHI.Common.Data.Zones;
using Dalamud.Bindings.ImGui;
using Dalamud.Plugin.Services;
using Ocelot.Services.UI;

namespace BOCCHI.Common.UI;

public static class MobPickerHelper
{
    public static bool Draw(
        List<Mob> selectedMobs,
        IDataManager data,
        IUIService ui,
        ref string search,
        ref int zoneFilterIndex,
        string searchHint,
        string selectedLabel,
        string zoneFilterLabel,
        string allZonesLabel,
        string southHornLabel,
        string northHornLabel,
        string listId,
        float listHeight = ImGuiSectionHelper.DefaultListHeight
    )
    {
        ImGui.TextUnformatted(zoneFilterLabel);
        ImGui.SetNextItemWidth(-1);
        if (ImGui.BeginCombo("##mob_zone_filter", ZoneFilterLabel(zoneFilterIndex, allZonesLabel, southHornLabel, northHornLabel)))
        {
            if (ImGui.Selectable(allZonesLabel, zoneFilterIndex == 0))
            {
                zoneFilterIndex = 0;
            }

            if (ImGui.Selectable(southHornLabel, zoneFilterIndex == 1))
            {
                zoneFilterIndex = 1;
            }

            if (ImGui.Selectable(northHornLabel, zoneFilterIndex == 2))
            {
                zoneFilterIndex = 2;
            }

            ImGui.EndCombo();
        }

        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##mob_search", searchHint, ref search, 128);

        ZoneId? zoneFilter = zoneFilterIndex switch
        {
            1 => ZoneId.SouthHorn,
            2 => ZoneId.NorthHorn,
            _ => null
        };

        string searchFilter = search;
        List<Mob> selectableMobs = MobData.GetSelectableMobs(zoneFilter)
            .Where(m => MobData.MatchesSearch(m, searchFilter, data))
            .ToList();

        int selectedShown = selectedMobs.Count(m => selectableMobs.Contains(m));
        ui.LabelledValue(selectedLabel, $"{selectedShown} / {selectableMobs.Count} shown");

        if (selectableMobs.Count == 0)
        {
            return false;
        }

        using ImGuiSectionHelper.BoundedListScope list =
            ImGuiSectionHelper.BoundedList(listId, selectableMobs.Count, listHeight);
        if (!list.IsOpen)
        {
            return false;
        }

        bool changed = false;
        foreach(Mob mob in selectableMobs)
        {
            bool isSelected = selectedMobs.Contains(mob);
            if (!ImGui.Checkbox($"{MobData.GetDisplayName(mob, data)}###mob_{(uint)mob}", ref isSelected))
            {
                continue;
            }

            changed = true;
            if (isSelected)
            {
                if (!selectedMobs.Contains(mob))
                {
                    selectedMobs.Add(mob);
                }
            }
            else
            {
                selectedMobs.Remove(mob);
            }
        }

        return changed;
    }

    public static int ZoneFilterIndexFor(ZoneId zoneId) => zoneId switch
    {
        ZoneId.SouthHorn => 1,
        ZoneId.NorthHorn => 2,
        _ => 0
    };

    private static string ZoneFilterLabel(int index, string allZonesLabel, string southHornLabel, string northHornLabel) =>
        index switch
        {
            1 => southHornLabel,
            2 => northHornLabel,
            _ => allZonesLabel
        };
}
