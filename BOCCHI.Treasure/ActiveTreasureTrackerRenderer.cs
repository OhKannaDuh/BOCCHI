using BOCCHI.Common;
using BOCCHI.Common.Config;
using BOCCHI.Treasure.Services;
using Ocelot.Services.Translation;
using Ocelot.Windows;

namespace BOCCHI.Treasure;

public class ActiveTreasureTrackerRenderer
(
    ITreasureTracker tracker,
    TreasureConfig treasureConfig,
    UIConfig uiConfig,
    ITranslator<MainWindow> translator
) : IDynamicRenderer
{
    public MainWindowSection Section => MainWindowSection.Trackers;

    public uint Order => 20;

    public void Render() =>
        ActiveTreasureCountUi.Draw(tracker, treasureConfig, translator, showIdleHint: true);

    public bool ShouldRender() => uiConfig.ShowActiveTreasureTracker;
}
