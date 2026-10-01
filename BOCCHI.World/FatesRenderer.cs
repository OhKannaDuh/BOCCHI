using BOCCHI.Common;
using BOCCHI.Common.Config;
using BOCCHI.Common.Data.EventDrops;
using BOCCHI.Common.Data.Fates;
using BOCCHI.Common.Data.Zones;
using BOCCHI.Common.Services;
using BOCCHI.Common.UI;
using Dalamud.Bindings.ImGui;
using Ocelot.Services.Translation;
using Ocelot.Windows;

namespace BOCCHI.Fates;

public class FatesRenderer
(
    IFateRepository fates,
    IActivityNavigation navigation,
    IZoneProvider zones,
    UIConfig uiConfig,
    EventDropIconRenderer eventDrops,
    ITranslator<MainWindow> translator
) : IDynamicRenderer
{
    public MainWindowSection Section => MainWindowSection.World;

    public string? SubsectionTitle => translator.T(".world.fates.title");

    public void Render()
    {
        List<Fate> snapshots = fates.Snapshot().ToList();
        if (snapshots.Count == 0)
        {
            BocchiUi.MutedText(translator.T(".world.fates.none"));
            return;
        }

        ZoneId zoneId = zones.GetZone().ZoneId;
        bool showDrops = zones.GetZone().IsOccultCrescentZone() && uiConfig.AnyEventDropsEnabled;
        float progressExtra = ImGui.GetFrameHeightWithSpacing();
        float dropExtra = EventDropIconRenderer.ListRowExtra(showDrops) + progressExtra;
        float maxHeight = EventDropIconRenderer.ListMaxHeight(showDrops);

        using ImGuiSectionHelper.BoundedListScope list =
            ImGuiSectionHelper.BoundedList("##fates_list", snapshots.Count, maxHeight, dropExtra);
        if (!list.IsOpen)
        {
            return;
        }

        foreach (Fate fate in snapshots)
        {
            string details = FormatFateDetails(fate);

            ActivitySnapshotRenderer.RenderCompactWithActions(
                navigation,
                fate.Name,
                details,
                fate.Position,
                $"fate_{fate.Id.Value}");

            if (fate.Progress is > 0 and < 100)
            {
                BocchiUi.DrawPercentBar(
                    fate.Progress / 100f,
                    Math.Min(180f, ImGui.GetContentRegionAvail().X),
                    $"{fate.Progress}%");
            }

            if (FieldNoteTargets.TryGetDropsForFate(zoneId, fate.Id.Value, out EventDropInfo drops))
            {
                eventDrops.Render(fate.Id.Value, drops);
            }
        }
    }

    private string FormatFateDetails(Fate fate)
    {
        string state = fate.State.ToString() switch
        {
            "Preparation" => translator.T(".world.fates.state_preparation"),
            "Running" => translator.T(".world.fates.state_running"),
            "Ending" => translator.T(".world.fates.state_ending"),
            _ => fate.State.ToString(),
        };

        return fate.Progress is > 0 and < 100
            ? $"{state} · {fate.Progress}%"
            : state;
    }

    public bool ShouldRender() => uiConfig.ShowWorldSection;
}
