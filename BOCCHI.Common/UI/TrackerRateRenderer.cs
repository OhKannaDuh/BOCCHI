using Dalamud.Bindings.ImGui;

namespace BOCCHI.Common.UI;

public static class TrackerRateRenderer
{
    public static void RenderPerHour(
        string label,
        double perHour,
        float[] history,
        string plotId,
        bool showGraph = true,
        float plotHeight = 30f
    )
    {
        BocchiUi.LabelledValue(label, perHour.ToString("N0"));

        if (showGraph)
        {
            PlotPerHourHistory(history, plotId, plotHeight);
        }
    }

    private static void PlotPerHourHistory(float[] history, string id, float height = 30f)
    {
        if (history.Length <= 0)
        {
            return;
        }

        float max = history.Max();
        if (max <= 0f)
        {
            max = 1f;
        }

        ImGui.PushStyleColor(ImGuiCol.PlotLines, BocchiUi.Header);
        ImGui.PlotLines(
            id,
            history.AsSpan(),
            history.Length,
            string.Empty,
            0f,
            max,
            new(ImGui.GetContentRegionAvail().X, height)
        );
        ImGui.PopStyleColor();
    }
}
