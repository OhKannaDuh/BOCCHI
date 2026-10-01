using BOCCHI.Automator.Services;
using BOCCHI.Common;
using BOCCHI.Common.Config;
using BOCCHI.Common.Data.EventDrops;
using BOCCHI.Common.Data.Fates;
using BOCCHI.Common.Data.Zones;
using BOCCHI.Common.Services;
using BOCCHI.Common.UI;
using BOCCHI.Treasure;
using BOCCHI.Treasure.Services;
using Dalamud.Bindings.ImGui;
using Ocelot.Extensions;
using Ocelot.Services.Translation;
using Ocelot.Windows;

namespace BOCCHI.Automator;

public class PotsTreasureRenderer
(
    Func<IPotsTreasureMode> potsTreasureFactory,
    ITreasureHunter hunter,
    TreasureConfig treasureConfig,
    UIConfig uiConfig,
    EventDropIconRenderer eventDrops,
    IFateRepository fates,
    IActivityNavigation navigation,
    IZoneProvider zones,
    ITranslator<MainWindow> translator
) : IDynamicRenderer
{
    private IPotsTreasureMode? potsTreasure;

    private IPotsTreasureMode PotsTreasure => potsTreasure ??= potsTreasureFactory();

    public MainWindowSection Section => MainWindowSection.PotsTreasure;

    public void Render()
    {
        ImGui.Spacing();

        if (!PotsTreasure.Running)
        {
            if (ImGui.Button(translator.T(".automation.pots_treasure.start")))
            {
                PotsTreasure.Toggle();
            }
        }
        else
        {
            if (PotsTreasure.Paused)
            {
                if (ImGui.Button(translator.T(".automation.pots_treasure.resume")))
                {
                    PotsTreasure.Resume();
                }

                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip(translator.T(".automation.pots_treasure.resume_tooltip"));
                }
            }
            else
            {
                if (ImGui.Button(translator.T(".automation.pots_treasure.pause")))
                {
                    PotsTreasure.Pause();
                }

                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip(translator.T(".automation.pots_treasure.pause_tooltip"));
                }
            }

            ImGui.SameLine();
            if (ImGui.Button(translator.T(".automation.pots_treasure.stop")))
            {
                PotsTreasure.Toggle();
            }
        }

        ImGui.Spacing();
        if (!PotsTreasure.Running)
        {
            BocchiUi.DrawIntro(translator.T(".automation.pots_treasure.description"));
        }

        DrawActivePotFates();

        if (!PotsTreasure.Running)
        {
            return;
        }

        ImGui.Spacing();
        BocchiUi.LabelledValue(
            translator.T(".automation.pots_treasure.phase"),
            translator.T($".automation.pots_treasure.phases.{PotsTreasure.Phase.ToString().ToSnakeCase()}"));

        if (PotsTreasure.Phase == PotsTreasurePhase.Hunting && !hunter.Running && !PotsTreasure.Paused)
        {
            if (ImGui.Button(translator.T(".automation.pots_treasure.resume_treasure_hunt")))
            {
                PotsTreasure.ResumeTreasureHunt();
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(translator.T(".automation.pots_treasure.resume_treasure_hunt_tooltip"));
            }
        }

        if (hunter.ManagedByPotsTreasure && (hunter.Running || hunter.Elapsed > TimeSpan.Zero))
        {
            ImGui.Spacing();
            if (hunter.Elapsed > TimeSpan.Zero)
            {
                BocchiUi.LabelledValue(translator.T(".treasure.elapsed"), $"{hunter.Elapsed:mm\\:ss}");
            }

            TreasureHuntStatusUi.DrawProgress(hunter, translator, treasureConfig);
        }
    }

    private void DrawActivePotFates()
    {
        IZone zone = zones.GetZone();
        if (!zone.IsOccultCrescentZone())
        {
            return;
        }

        List<Fate> potFates = fates.Snapshot()
            .Where(f => zone.IsPotFate(f.Id.Value))
            .ToList();

        ImGui.Spacing();
        BocchiUi.SectionTitle(translator.T(".automation.pots_treasure.active_fates"));

        if (potFates.Count == 0)
        {
            BocchiUi.MutedText(translator.T(".automation.pots_treasure.no_active_fate"));
            return;
        }

        bool showDrops = uiConfig.AnyEventDropsEnabled;
        float dropExtra = EventDropIconRenderer.ListRowExtra(showDrops);
        float maxHeight = EventDropIconRenderer.ListMaxHeight(showDrops);

        using ImGuiSectionHelper.BoundedListScope list =
            ImGuiSectionHelper.BoundedList("##pots_treasure_fates", potFates.Count, maxHeight, dropExtra);
        if (!list.IsOpen)
        {
            return;
        }

        ZoneId zoneId = zone.ZoneId;
        foreach (Fate fate in potFates)
        {
            string details = $"{fate.State} {fate.Progress}% · #{fate.Id.Value}";
            if (fate.TimeRemainingSeconds > 0)
            {
                details += $" · {TimeSpan.FromSeconds(fate.TimeRemainingSeconds):mm\\:ss}";
            }

            ActivitySnapshotRenderer.RenderCompactWithActions(
                navigation,
                fate.Name,
                details,
                fate.Position,
                $"pot_fate_{fate.Id.Value}");

            if (FieldNoteTargets.TryGetDropsForFate(zoneId, fate.Id.Value, out EventDropInfo drops))
            {
                eventDrops.Render(fate.Id.Value, drops);
            }
        }
    }

    public bool ShouldRender() => uiConfig.ShowPotsTreasureSection;
}
