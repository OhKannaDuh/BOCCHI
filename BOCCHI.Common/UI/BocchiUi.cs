using Dalamud.Bindings.ImGui;
using System.Numerics;

namespace BOCCHI.Common.UI;

public static class BocchiUi
{
    public enum StatusChipKind
    {
        Ok,
        Warn,
        Muted,
    }

    public static readonly Vector4 Header = new(0.90f, 0.78f, 0.42f, 1f);
    public static readonly Vector4 Muted = new(0.72f, 0.72f, 0.74f, 1f);
    public static readonly Vector4 Warn = new(0.98f, 0.82f, 0.42f, 1f);
    public static readonly Vector4 Good = new(0.55f, 0.92f, 0.62f, 1f);
    public static readonly Vector4 Bad = new(0.98f, 0.52f, 0.52f, 1f);

    private static readonly Vector4 PanelBg = new(0.10f, 0.10f, 0.12f, 0.55f);
    private static readonly Vector4 PanelBorder = new(0.40f, 0.40f, 0.45f, 0.55f);

    private static readonly Vector4 ChipOkBg = new(0.14f, 0.32f, 0.20f, 0.95f);
    private static readonly Vector4 ChipWarnBg = new(0.38f, 0.28f, 0.08f, 0.95f);
    private static readonly Vector4 ChipMutedBg = new(0.20f, 0.20f, 0.23f, 0.95f);
    private static readonly Vector4 ChipOkFg = new(0.78f, 0.96f, 0.82f, 1f);
    private static readonly Vector4 ChipWarnFg = new(1.00f, 0.92f, 0.68f, 1f);
    private static readonly Vector4 ChipMutedFg = new(0.88f, 0.88f, 0.90f, 1f);

    private static readonly Vector4 BarGood = new(0.22f, 0.58f, 0.34f, 1f);
    private static readonly Vector4 BarWarn = new(0.58f, 0.44f, 0.14f, 1f);
    private static readonly Vector4 BarEmpty = new(0.22f, 0.22f, 0.25f, 1f);

    public const float PanelPadX = 12f;
    public const float PanelPadY = 10f;
    public const float PanelRounding = 5f;
    public const float FieldFrameRounding = 4f;

    private static readonly Stack<PanelScope> PanelStack = new();

    private readonly struct PanelScope
    {
        public required bool UseChild { get; init; }

        public float Width { get; init; }
    }

    public static void SectionTitle(string text)
    {
        ImGui.TextColored(Header, text);
    }

    public static void MutedText(string text)
    {
        ImGui.TextColored(Muted, text);
    }

    public static void MutedWrapped(string text)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, Muted);
        ImGui.TextWrapped(text);
        ImGui.PopStyleColor();
    }

    public static void LabelledValue(string label, string value)
    {
        ImGui.TextColored(Header, label);
        ImGui.SameLine(0f, 8f);
        MutedText(value);
    }

    public static void LabelledValue(string label, object value) =>
        LabelledValue(label, value.ToString() ?? string.Empty);

    public static void DrawIntro(string blurb)
    {
        MutedWrapped(blurb);
        ImGui.Dummy(new Vector2(0, 4));
    }

    public static void EndStickyHeader()
    {
        ImGui.Dummy(new Vector2(0, 2));
        ImGui.Separator();
        ImGui.Dummy(new Vector2(0, 6));
    }

    public static bool BeginPanel(string id, float height = 0f)
    {
        if (height != 0f)
        {
            ImGui.PushStyleColor(ImGuiCol.ChildBg, PanelBg);
            ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, PanelRounding);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(PanelPadX, PanelPadY));
            if (!ImGui.BeginChild($"##bocchi_panel_{id}", new Vector2(0, height), true))
            {
                ImGui.EndChild();
                ImGui.PopStyleVar(2);
                ImGui.PopStyleColor();
                return false;
            }

            PanelStack.Push(new PanelScope { UseChild = true });
            return true;
        }

        ImGui.PushID(id);
        float width = ImGui.GetContentRegionAvail().X;
        PanelStack.Push(new PanelScope { UseChild = false, Width = width });

        ImDrawListPtr drawList = ImGui.GetWindowDrawList();
        drawList.ChannelsSplit(2);
        drawList.ChannelsSetCurrent(1);

        ImGui.BeginGroup();
        ImGui.Dummy(new Vector2(width, PanelPadY));
        ImGui.Indent(PanelPadX);
        return true;
    }

    public static void EndPanel()
    {
        if (PanelStack.Count == 0)
        {
            return;
        }

        PanelScope scope = PanelStack.Pop();
        if (scope.UseChild)
        {
            ImGui.EndChild();
            ImGui.PopStyleVar(2);
            ImGui.PopStyleColor();
            ImGui.Dummy(new Vector2(0, 8));
            return;
        }

        ImGui.Unindent(PanelPadX);
        ImGui.Dummy(new Vector2(scope.Width, PanelPadY));
        ImGui.EndGroup();

        Vector2 min = ImGui.GetItemRectMin();
        Vector2 max = ImGui.GetItemRectMax();
        max.X = min.X + scope.Width;

        ImDrawListPtr drawList = ImGui.GetWindowDrawList();
        drawList.ChannelsSetCurrent(0);
        drawList.AddRectFilled(min, max, ImGui.ColorConvertFloat4ToU32(PanelBg), PanelRounding);
        drawList.AddRect(min, max, ImGui.ColorConvertFloat4ToU32(PanelBorder), PanelRounding);
        drawList.ChannelsMerge();

        ImGui.PopID();
        ImGui.Dummy(new Vector2(0, 8));
    }

    public static bool DrawStatusChip(string label, StatusChipKind kind)
    {
        Vector4 bg = kind switch
        {
            StatusChipKind.Ok => ChipOkBg,
            StatusChipKind.Warn => ChipWarnBg,
            _ => ChipMutedBg,
        };
        Vector4 fg = kind switch
        {
            StatusChipKind.Ok => ChipOkFg,
            StatusChipKind.Warn => ChipWarnFg,
            _ => ChipMutedFg,
        };

        ImGui.PushStyleColor(ImGuiCol.Button, bg);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, bg);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, bg);
        ImGui.PushStyleColor(ImGuiCol.Text, fg);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 11f);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(10f, 3f));
        bool clicked = ImGui.SmallButton(label);
        ImGui.PopStyleVar(2);
        ImGui.PopStyleColor(4);
        return clicked;
    }

    public static void DrawPercentBar(float fraction, float width, string overlay)
    {
        float clamped = Math.Clamp(fraction, 0f, 1f);
        Vector4 fill = clamped >= 1f ? BarGood : clamped > 0f ? BarWarn : BarEmpty;

        float gap = 8f;
        float labelW = string.IsNullOrEmpty(overlay) ? 0f : ImGui.CalcTextSize(overlay).X + gap;
        float barW = Math.Max(48f, width - labelW);

        ImGui.PushStyleColor(ImGuiCol.PlotHistogram, fill);
        ImGui.ProgressBar(clamped, new Vector2(barW, ImGui.GetFrameHeight()), string.Empty);
        ImGui.PopStyleColor();

        if (!string.IsNullOrEmpty(overlay))
        {
            ImGui.SameLine(0f, gap);
            ImGui.AlignTextToFramePadding();
            MutedText(overlay);
        }
    }

    public static void PushFieldStyle()
    {
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, FieldFrameRounding);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(8f, 4f));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(8f, 6f));
    }

    public static void PopFieldStyle() => ImGui.PopStyleVar(3);
}
