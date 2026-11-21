using BOCCHI.Common;
using BOCCHI.Common.Data;
using BOCCHI.Common.Config;
using BOCCHI.Common.UI;
using BOCCHI.Currency.Services;
using Ocelot.Services.Translation;
using Ocelot.Windows;

namespace BOCCHI.Currency;

public class CurrencyRenderer
(
    ICurrencyTracker tracker,
    UIConfig uiConfig,
    ITranslator<MainWindow> translator
) : IDynamicRenderer
{
    public MainWindowSection Section => MainWindowSection.Trackers;

    public uint Order => 10;

    public string CompactText => string.Format(
        translator.T(".trackers.currency.compact"),
        tracker.GoldPerHour.ToString("N0"),
        tracker.SilverPerHour.ToString("N0"));

    public void Render()
    {
        TimeSpan graphBucketSize = DeltaRateTracker.DefaultGraphBucket;

        TrackerRateRenderer.RenderPerHour(
            translator.T(".trackers.currency.gold_per_hour"),
            tracker.GoldPerHour,
            tracker.GetGoldHistory(graphBucketSize),
            "##gold_history",
            uiConfig.ShowCurrencyTrackerGraph
        );

        TrackerRateRenderer.RenderPerHour(
            translator.T(".trackers.currency.silver_per_hour"),
            tracker.SilverPerHour,
            tracker.GetSilverHistory(graphBucketSize),
            "##silver_history",
            uiConfig.ShowCurrencyTrackerGraph
        );
    }

    public bool ShouldRender() => uiConfig.ShowCurrencyTracker;
}
