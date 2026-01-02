using BOCCHI.Common;
using BOCCHI.Common.Services;
using BOCCHI.CriticalEncounters.Services;
using Dalamud.Bindings.ImGui;
using Ocelot.Services.UI;

namespace BOCCHI.CriticalEncounters;

public class CriticalEncountersRenderer(
    ICriticalEncounterRepository criticalEncounters,
    IBrandingService branding,
    IUIService ui
) : IDynamicRenderer
{
    public void Render()
    {
        var snapshot = criticalEncounters.SnapshotWithoutForkedTower();
        foreach (var criticalEncounter in snapshot)
        {
            ui.Text(criticalEncounter.Id, branding.DalamudYellow);
            ImGui.Indent(32);

            ui.LabelledValue("Id", criticalEncounter.Id);
            ui.LabelledValue("Position", criticalEncounter.Position.ToString("f2"));
            ui.LabelledValue("State", criticalEncounter.State);

            ImGui.Unindent(32);
        }
    }

    public bool ShouldRender()
    {
        return true;
    }
}
