using BOCCHI.Common.Config;
using BOCCHI.Common.Data.EventDrops;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using Lumina.Excel.Sheets;
using Ocelot.Services.Translation;
using Ocelot.Windows;
using System.Numerics;

namespace BOCCHI.Common.UI;

public class EventDropIconRenderer(
    IDataManager data,
    ITextureProvider textures,
    UIConfig config,
    ITranslator<MainWindow> translator)
{
    public const float IconBoxSize = 50f;

    public static float ListRowExtra(bool showDrops) =>
        showDrops ? IconBoxSize + 4f : 0f;

    public static float ListMaxHeight(bool showDrops) =>
        showDrops ? 240f : 120f;

    private bool WouldRender(EventDropInfo drops) =>
        (drops.Demiatma is not null && config.ShowDemiatmaDrops)
        || (drops.Notes is not null && config.ShowNoteDrops)
        || (drops.SoulShard is not null && config.ShowSoulShardDrops);

    public void Render(uint activityId, EventDropInfo drops)
    {
        if (!WouldRender(drops))
        {
            return;
        }

        ImGui.Indent(12f);
        uint rendered = 0;

        if (drops.Demiatma is { } demiatma && config.ShowDemiatmaDrops)
        {
            RenderDemiatma(activityId, (uint)demiatma);
            rendered++;
        }

        if (drops.Notes is { } notes && config.ShowNoteDrops)
        {
            if (rendered > 0)
            {
                ImGui.SameLine();
            }

            RenderItemIcon(activityId, (uint)notes, "Note", Vector4.One);
            rendered++;
        }

        if (drops.SoulShard is { } shard && config.ShowSoulShardDrops)
        {
            if (rendered > 0)
            {
                ImGui.SameLine();
            }

            RenderItemIcon(activityId, (uint)shard, "SoulShard", Vector4.One);
        }

        ImGui.Unindent(12f);
    }

    private unsafe void RenderDemiatma(uint activityId, uint itemId)
    {
        if (!data.GetExcelSheet<Item>().TryGetRow(itemId, out Item item))
        {
            return;
        }

        int count = InventoryManager.Instance()->GetInventoryItemCount(itemId);
        int needed = Math.Max(0, 3 - count);
        Vector4 border = needed > 0
            ? new Vector4(0.3f, 0.85f, 0.39f, 1f)
            : new Vector4(0.95f, 0.26f, 0.21f, 1f);

        DrawIcon(item.Icon, border, $"Demiatma_{itemId}_{activityId}");

        if (ImGui.IsItemHovered())
        {
            string label = needed > 0
                ? translator.T(".event_drops.demiatma_needed", ("count", needed))
                : translator.T(".event_drops.demiatma_not_needed", ("count", count));
            ImGui.SetTooltip($"{item.Name}: {label}");
        }
    }

    private void RenderItemIcon(uint activityId, uint itemId, string kind, Vector4 border)
    {
        if (!data.GetExcelSheet<Item>().TryGetRow(itemId, out Item item))
        {
            return;
        }

        DrawIcon(item.Icon, border, $"{kind}_{itemId}_{activityId}");

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(item.Name.ToString());
        }
    }

    private void DrawIcon(uint iconId, Vector4 border, string id)
    {
        IDalamudTextureWrap icon = textures.GetFromGameIcon(new GameIconLookup(iconId)).GetWrapOrEmpty();

        ImGui.PushStyleColor(ImGuiCol.Border, border);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);

        ImGui.BeginChild($"ImageBorder##{id}", new Vector2(IconBoxSize, IconBoxSize), true, ImGuiWindowFlags.NoScrollbar);
        ImGui.Image(icon.Handle, new Vector2(IconBoxSize - 2f, IconBoxSize - 2f));
        ImGui.EndChild();

        ImGui.PopStyleVar();
        ImGui.PopStyleColor();
    }
}
