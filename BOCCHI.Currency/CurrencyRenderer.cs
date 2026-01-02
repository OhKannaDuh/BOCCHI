using System.Numerics;
using BOCCHI.Common;
using BOCCHI.Common.Config;
using BOCCHI.Currency.Services;
using Dalamud.Bindings.ImGui;
using Ocelot.Services.UI;

namespace BOCCHI.Currency;

public class CurrencyRenderer(
    ICurrencyTracker tracker,
    CurrencyConfig config,
    UIConfig uiConfig,
    IBrandingService branding,
    IUIService ui
) : IDynamicRenderer
{
    public void Render()
    {
        if (!uiConfig.ShowCurrencyTracker)
        {
            return;
        }

        var graphBucketSize = TimeSpan.FromSeconds(config.GraphBucketSize);

        ui.Render(
            ui.Compose().Text("Gold Per Hour", branding.DalamudYellow),
            ui.Compose().Text(tracker.GoldPerHour.ToString("f2"))
        );

        RenderGraph(tracker.GetGoldHistory(graphBucketSize), "gold_history");

        ui.Render(
            ui.Compose().Text("Silver Per Hour", branding.DalamudYellow),
            ui.Compose().Text(tracker.SilverPerHour.ToString("f2"))
        );

        RenderGraph(tracker.GetSilverHistory(graphBucketSize), "silver_history");
    }

    public bool ShouldRender()
    {
        return uiConfig.ShowCurrencyTracker;
    }

    private void RenderGraph(float[] history, string id)
    {
        if (!uiConfig.ShowCurrencyTrackerGraph)
        {
            return;
        }

        if (history.Length <= 0)
        {
            return;
        }

        var max = history.Max();
        if (max <= 0f)
        {
            max = 1f;
        }

        var size = new Vector2(ImGui.GetContentRegionAvail().X, 30);

        ImGui.PlotLines(
            $"##{id}",
            history.AsSpan(),
            history.Length,
            string.Empty,
            0f,
            max,
            size,
            sizeof(float)
        );
    }
}
