using BOCCHI.Common.Config;
using BOCCHI.Common.Data.Fates;
using BOCCHI.Common.Data.Zones;
using BOCCHI.Common.Services;
using Dalamud.Plugin.Services;
using Ocelot.Extensions;

namespace BOCCHI.Fates.Services;

public class FateScorer
(
    AutomatorConfig automatorConfig,
    FatesConfig config,
    PotsConfig potsConfig,
    IZoneProvider zones,
    IObjectTable objects
) : IFateScorer
{
    private const float PotFateBonus = 250f;

    public FateScore Score(Fate fate)
    {
        FateScore score = new();

        bool isPot = zones.GetZone().IsPotFate(fate.Id.Value);
        bool potAllowed = isPot && (automatorConfig.ShouldFarmPotChests || automatorConfig.ShouldPrepositionToPots);
        if ((!automatorConfig.ShouldDoFates && !potAllowed)
            || !config.IsFateEnabledForIllegalMode(fate.Id.Value, isPot, automatorConfig.PreferPotFates))
        {
            return score;
        }

        if (ShouldSkip(fate))
        {
            return score;
        }

        if (objects.LocalPlayer is not { } player)
        {
            return score;
        }

        float distance = player.Position.Distance2D(fate.Position);
        score.Add("distance", 1000f / (distance + 1f));
        score.Add("progress", Math.Max(0, 100 - fate.Progress));

        if (automatorConfig.PreferPotFates && zones.GetZone().IsPotFate(fate.Id.Value))
        {
            score.Add("pot", PotFateBonus);
        }

        return score;
    }


    private bool ShouldSkip(Fate fate) =>
        config.ShouldSkipByProgress(fate.Progress)
        || (zones.GetZone().IsPotFate(fate.Id.Value)
            && potsConfig.ShouldSkipLivePot(fate.TimeRemainingSeconds));
}
