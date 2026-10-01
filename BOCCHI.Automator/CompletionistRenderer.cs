using BOCCHI.Automator.Services;
using BOCCHI.Common;
using BOCCHI.Common.Config;
using BOCCHI.Common.Data.EventDrops;
using BOCCHI.Common.Data.StateMemory;
using BOCCHI.Common.Data.SupportJobs;
using BOCCHI.Common.Data.Zones;
using BOCCHI.Common.Services;
using BOCCHI.Common.UI;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;
using Ocelot.Services.Translation;
using Ocelot.Windows;
using System.Numerics;

namespace BOCCHI.Automator;

public class CompletionistRenderer
(
    IAutomator automator,
    IAutomatorMemory memory,
    IActivityNavigation navigation,
    UIConfig uiConfig,
    IFieldNoteTracker fieldNotes,
    ISupportJobFactory supportJobs,
    IZoneProvider zones,
    IDataManager data,
    IGameGui gameGui,
    ITranslator<MainWindow> translator
) : IDynamicRenderer
{
    public MainWindowSection Section => MainWindowSection.Completionist;

    public void Render()
    {
        if (ImGui.Button(automator.IsCompletionist
                ? translator.T(".completionist.disable")
                : translator.T(".completionist.enable")))
        {
            automator.ToggleCompletionist();
        }

        if (automator.IsCompletionist)
        {
            AutomatorPathControls.Draw(automator, zones, translator, showRefresh: true);
        }
        else
        {
            AutomatorPathControls.Draw(automator, zones, translator, showRefresh: false);
        }

        ImGui.Spacing();

        ImGui.PushStyleColor(ImGuiCol.Text, BocchiUi.Header);
        bool aboutOpen = ImGui.CollapsingHeader(
            translator.T(".completionist.about"),
            ImGuiTreeNodeFlags.None);
        ImGui.PopStyleColor();
        if (aboutOpen)
        {
            BocchiUi.DrawIntro(translator.T(".completionist.description"));
            BocchiUi.MutedText(translator.T(".completionist.legend"));
        }

        ImGui.Spacing();

        if (ImGui.BeginTable("##completionist_checklist", 2, ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn("notes", ImGuiTableColumnFlags.WidthStretch, 1.2f);
            ImGui.TableSetupColumn("jobs", ImGuiTableColumnFlags.WidthStretch, 1f);

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            BocchiUi.SectionTitle(translator.T(".completionist.notes_header"));
            ImGui.TableNextColumn();
            BocchiUi.SectionTitle(translator.T(".completionist.jobs_header"));

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            RenderNotes();

            ImGui.TableNextColumn();
            RenderJobs();

            ImGui.EndTable();
        }
    }

    public bool ShouldRender() => uiConfig.ShowCompletionistSection;

    private void RenderNotes()
    {
        IZone zone = zones.GetZone();
        IReadOnlyList<FieldNoteTargets.Entry> entries = FieldNoteTargets.ChecklistFor(zone.ZoneId);
        if (entries.Count == 0)
        {
            BocchiUi.MutedWrapped(translator.T(".completionist.outside_zone"));
            return;
        }

        foreach (FieldNoteTargets.Entry entry in entries)
        {
            bool owned = fieldNotes.HasEntry(entry);
            string source = translator.T($".completionist.sources.{entry.SourceKey}");
            string noteName = ResolveEntryName(entry);
            string label = $"{source} — {noteName}";
            Vector4 color = owned ? BocchiUi.Good : BocchiUi.Muted;
            FontAwesomeIcon icon = owned ? FontAwesomeIcon.Check : FontAwesomeIcon.Times;

            if (entry.CanFlag)
            {
                DrawSurveyRow(entry, icon, color, label, noteName);
            }
            else
            {
                DrawIconRow(icon, color, label);
            }
        }
    }

    private void DrawSurveyRow(
        FieldNoteTargets.Entry entry,
        FontAwesomeIcon icon,
        Vector4 color,
        string label,
        string noteName)
    {
        using (ImRaii.PushFont(UiBuilder.IconFont))
        {
            ImGui.TextColored(color, icon.ToIconString());
        }

        ImGui.SameLine();
        ImGui.PushStyleColor(ImGuiCol.Text, color);
        bool clicked = ImGui.Selectable($"{label}##survey_{entry.MkdLoreId}");
        ImGui.PopStyleColor();

        if (ImGui.IsItemHovered())
        {
            bool canTravel = navigation.CanPathfind && entry.CanPath;
            ImGui.SetTooltip(translator.T(
                canTravel
                    ? ".completionist.survey_tooltip"
                    : ".completionist.survey_tooltip_flag_only"));
        }

        if (clicked)
        {
            bool travel = ImGui.GetIO().KeyCtrl;
            GoToSurvey(entry, noteName, travel);
        }
    }

    private void GoToSurvey(FieldNoteTargets.Entry entry, string noteName, bool travel)
    {
        float mapX = entry.MapX!.Value;
        float mapY = entry.MapY!.Value;
        if (!TryResolveSurveyLink(mapX, mapY, out MapLinkPayload link))
        {
            return;
        }

        gameGui.OpenMapWithMapLink(link);

        if (!travel || !entry.CanPath || !navigation.CanPathfind)
        {
            return;
        }

        if (automator.IsActive)
        {
            memory.Forget<GoalMemory>();
            IllegalModeActivityWork.ForgetTravelLatches(memory);
            automator.SoftStopPathfinding();
            memory.Forget<NavigationInterruptedMemory>();
            memory.TryAdd(new NavigationInterruptedMemory());
        }

        string title = $"{translator.T(".completionist.sources.survey_point")} — {noteName}";
        navigation.PathToPoint(entry.WorldPosition!.Value, title, $"survey_{entry.MkdLoreId}");
    }

    private bool TryResolveSurveyLink(float mapX, float mapY, out MapLinkPayload link)
    {
        link = null!;

        IZone zone = zones.GetZone();
        uint territoryId = zone.TerritoryType;
        if (!data.GetExcelSheet<TerritoryType>().TryGetRow(territoryId, out TerritoryType territory))
        {
            return false;
        }

        uint mapId = territory.Map.RowId;
        if (mapId == 0)
        {
            return false;
        }

        link = new MapLinkPayload(territoryId, mapId, mapX, mapY);
        return true;
    }

    private void RenderJobs()
    {
        foreach (SupportJob job in supportJobs.All().OrderBy(j => j.Id))
        {
            byte level = job.Level;
            bool unlocked = level >= 1;
            bool maxed = unlocked && job.Data.LevelMax > 0 && level >= job.Data.LevelMax;
            string name = job.Data.Name.ToString();
            if (string.IsNullOrWhiteSpace(name))
            {
                name = job.Id.ToString();
            }

            if (maxed)
            {
                DrawIconRow(FontAwesomeIcon.Star, BocchiUi.Header, name);
            }
            else if (unlocked)
            {
                DrawIconRow(FontAwesomeIcon.Check, BocchiUi.Good, name);
            }
            else
            {
                DrawIconRow(FontAwesomeIcon.Times, BocchiUi.Muted, name);
            }
        }
    }

    private static void DrawIconRow(FontAwesomeIcon icon, Vector4 color, string label)
    {
        using (ImRaii.PushFont(UiBuilder.IconFont))
        {
            ImGui.TextColored(color, icon.ToIconString());
        }

        ImGui.SameLine();
        ImGui.TextColored(color, label);
    }

    private string ResolveEntryName(FieldNoteTargets.Entry entry)
    {
        if (data.GetExcelSheet<MKDLore>().TryGetRow(entry.MkdLoreId, out MKDLore lore))
        {
            string loreName = lore.Name.ToString();
            if (!string.IsNullOrWhiteSpace(loreName))
            {
                return loreName;
            }
        }

        if (entry.Note is { } note
            && data.GetExcelSheet<Item>().TryGetRow((uint)note, out Item item))
        {
            string itemName = item.Name.ToString();
            if (!string.IsNullOrWhiteSpace(itemName))
            {
                return itemName;
            }
        }

        return entry.Note?.ToString() ?? $"#{entry.MkdLoreId}";
    }
}
