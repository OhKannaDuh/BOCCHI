using BOCCHI.Common.Config;
using BOCCHI.Common.UI;
using BOCCHI.Treasure.Hunt;
using BOCCHI.Treasure.Services;
using Dalamud.Bindings.ImGui;
using Ocelot.Services.Translation;
using Ocelot.Windows;

namespace BOCCHI.Treasure;

public static class ActiveTreasureCountUi
{
    public static void Draw(
        ITreasureTracker tracker,
        TreasureConfig config,
        ITranslator<MainWindow> translator,
        bool showIdleHint = false)
    {
        if (!tracker.CountInitialised)
        {
            if (showIdleHint)
            {
                BocchiUi.MutedWrapped(translator.T(".treasure.active_counts_idle"));
            }

            return;
        }

        BocchiUi.SectionTitle(translator.T(".treasure.active_bronze"));
        float bronzeFraction = tracker.BronzeChests / (float)TreasureHuntFillGate.BronzeCap;
        string bronzeOverlay = FormatOverlay(
            tracker.BronzeChests,
            TreasureHuntFillGate.BronzeCap,
            bronzeFraction,
            config.ShowPercentageActiveTreasureCount);
        BocchiUi.DrawPercentBar(
            bronzeFraction,
            Math.Min(220f, ImGui.GetContentRegionAvail().X),
            bronzeOverlay);

        BocchiUi.SectionTitle(translator.T(".treasure.active_silver"));
        float silverFraction = tracker.SilverChests / (float)TreasureHuntFillGate.SilverCap;
        string silverOverlay = FormatOverlay(
            tracker.SilverChests,
            TreasureHuntFillGate.SilverCap,
            silverFraction,
            config.ShowPercentageActiveTreasureCount);
        BocchiUi.DrawPercentBar(
            silverFraction,
            Math.Min(220f, ImGui.GetContentRegionAvail().X),
            silverOverlay);
    }

    private static string FormatOverlay(int count, int cap, float fraction, bool showPercent) =>
        showPercent
            ? $"{count}/{cap} ({fraction * 100f:F2}%)"
            : $"{count}/{cap}";
}
