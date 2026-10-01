using BOCCHI.Common.Data.Zones;
using Ocelot.Services.Translation;
using Ocelot.Windows;

namespace BOCCHI.Common.UI;

public static class ZoneGraphStatusUi
{
    public static bool TryFormat(
        IZone zone,
        ITranslator<MainWindow> translator,
        out string label,
        out string value,
        out bool emphasize)
    {
        label = translator.T(".automation.automator.path_map");
        emphasize = false;

        if (!zone.IsOccultCrescentZone())
        {
            value = string.Empty;
            return false;
        }

        switch (zone.GraphLoadState)
        {
            case ZoneGraphLoadState.Loading:
                value = translator.T(".automation.automator.path_map_loading");
                emphasize = true;
                return true;

            case ZoneGraphLoadState.Building:
                value = translator.T(".automation.automator.path_map_building");
                emphasize = true;
                return true;

            case ZoneGraphLoadState.Ready:
                value = zone.GraphSource switch
                {
                    ZoneGraphSource.Shipped => translator.T(".automation.automator.path_map_ready_shipped"),
                    ZoneGraphSource.Built => translator.T(".automation.automator.path_map_ready_built"),
                    _ => translator.T(".automation.automator.path_map_ready_cache"),
                };
                return true;

            default:
                value = translator.T(".automation.automator.path_map_idle");
                return true;
        }
    }
}
