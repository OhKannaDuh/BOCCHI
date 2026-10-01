using System.Numerics;
using Dalamud.Bindings.ImGui;
using Ocelot.Services.Translation;

namespace BOCCHI.Common.Services.Logging;

public sealed class BocchiLogsPanel
(
    IBocchiLogBuffer buffer,
    IBocchiLogClipboard clipboard
)
{
    private string searchFilter = string.Empty;

    private BocchiLogLevel minLevel = BocchiLogLevel.Debug;

    private string? copiedFlash;

    private DateTime copiedFlashUntil;

    public void Draw(ITranslator translator, string idSuffix = "")
    {
        ImGui.SetNextItemWidth(220f);
        ImGui.InputTextWithHint($"##bocchi_log_search{idSuffix}", translator.T(".search_hint"), ref searchFilter, 256);

        ImGui.SameLine();
        ImGui.SetNextItemWidth(120f);
        if (ImGui.BeginCombo($"##bocchi_log_level{idSuffix}", translator.T($".level.{minLevel.ToString().ToLowerInvariant()}")))
        {
            foreach (BocchiLogLevel level in Enum.GetValues<BocchiLogLevel>())
            {
                bool selected = level == minLevel;
                if (ImGui.Selectable(translator.T($".level.{level.ToString().ToLowerInvariant()}"), selected))
                {
                    minLevel = level;
                }

                if (selected)
                {
                    ImGui.SetItemDefaultFocus();
                }
            }

            ImGui.EndCombo();
        }

        ImGui.SameLine();
        if (ImGui.Button($"{translator.T(".copy_all")}##{idSuffix}"))
        {
            clipboard.CopyAll(announceInChat: false);
            copiedFlash = translator.T(".copied");
            copiedFlashUntil = DateTime.UtcNow.AddSeconds(2);
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(translator.T(".copy_all_tooltip"));
        }

        ImGui.SameLine();
        if (ImGui.Button($"{translator.T(".clear")}##{idSuffix}"))
        {
            buffer.Clear();
        }

        if (copiedFlash is not null && DateTime.UtcNow < copiedFlashUntil)
        {
            ImGui.SameLine();
            ImGui.TextDisabled(copiedFlash);
        }
        else
        {
            copiedFlash = null;
        }

        ImGui.Spacing();

        float tableHeight = Math.Max(ImGui.GetContentRegionAvail().Y - 4f, 200f);
        ImGuiTableFlags flags = ImGuiTableFlags.RowBg
                                | ImGuiTableFlags.Borders
                                | ImGuiTableFlags.ScrollY
                                | ImGuiTableFlags.SizingFixedFit
                                | ImGuiTableFlags.Resizable;

        if (!ImGui.BeginTable($"##bocchi_log_table{idSuffix}", 4, flags, new Vector2(0, tableHeight)))
        {
            return;
        }

        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableSetupColumn(translator.T(".col_time"), ImGuiTableColumnFlags.WidthFixed, 110f);
        ImGui.TableSetupColumn(translator.T(".col_count"), ImGuiTableColumnFlags.WidthFixed, 50f);
        ImGui.TableSetupColumn(translator.T(".col_level"), ImGuiTableColumnFlags.WidthFixed, 70f);
        ImGui.TableSetupColumn(translator.T(".col_message"), ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableHeadersRow();

        IEnumerable<BocchiLogEntry> rows = buffer.Snapshot()
            .Where(e => e.Level >= minLevel)
            .Where(e =>
                string.IsNullOrWhiteSpace(searchFilter)
                || e.Message.Contains(searchFilter, StringComparison.OrdinalIgnoreCase)
                || e.Level.ToString().Contains(searchFilter, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(e => e.LastOccurrence);

        foreach (BocchiLogEntry log in rows)
        {
            ImGui.TableNextRow();

            ImGui.TableNextColumn();
            if (log.Count > 1)
            {
                ImGui.TextUnformatted($"{log.Timestamp:HH:mm:ss}");
                ImGui.SameLine(0, 4);
                ImGui.TextDisabled("-");
                ImGui.SameLine(0, 4);
                ImGui.TextUnformatted($"{log.LastOccurrence:HH:mm:ss}");
            }
            else
            {
                ImGui.TextUnformatted($"{log.Timestamp:HH:mm:ss}");
            }

            ImGui.TableNextColumn();
            if (log.Count > 1)
            {
                ImGui.TextColored(new Vector4(1f, 0.55f, 0.1f, 1f), $"x{log.Count}");
            }
            else
            {
                ImGui.TextDisabled("1");
            }

            ImGui.TableNextColumn();
            ImGui.TextColored(LevelColor(log.Level), log.Level.ToString());

            ImGui.TableNextColumn();
            ImGui.TextWrapped(log.Message);
        }

        ImGui.EndTable();
    }

    private static Vector4 LevelColor(BocchiLogLevel level) => level switch
    {
        BocchiLogLevel.Error => new Vector4(1f, 0.35f, 0.35f, 1f),
        BocchiLogLevel.Warning => new Vector4(1f, 0.9f, 0.2f, 1f),
        BocchiLogLevel.Info => new Vector4(0.4f, 0.9f, 1f, 1f),
        BocchiLogLevel.Debug => new Vector4(0.75f, 0.75f, 0.75f, 1f),
        _ => new Vector4(0.55f, 0.55f, 0.55f, 1f),
    };
}
