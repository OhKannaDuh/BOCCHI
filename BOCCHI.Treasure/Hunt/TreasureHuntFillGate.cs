using BOCCHI.Common.Config;
using BOCCHI.Treasure.Services;

namespace BOCCHI.Treasure.Hunt;

public static class TreasureHuntFillGate
{
    public const int BronzeCap = 30;

    public const int SilverCap = 8;

    public static bool MeetsMinimumFill(ITreasureTracker tracker, TreasureConfig config)
    {
        if (!tracker.CountInitialised)
        {
            return false;
        }

        float bronzePct = tracker.BronzeChests / (float)BronzeCap * 100f;
        float silverPct = tracker.SilverChests / (float)SilverCap * 100f;
        bool bronzeOk = bronzePct >= config.HuntMinBronzePercent;
        bool silverOk = silverPct >= config.HuntMinSilverPercent;
        if (config.HuntSilverChestsOnly)
        {
            return silverOk;
        }

        return bronzeOk || silverOk;
    }
}
