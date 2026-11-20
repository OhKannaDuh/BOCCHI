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

    public Fate? SelectBest(IReadOnlyList<Fate> fates)
    {
        if (!automatorConfig.ShouldDoFates || fates.Count == 0)
        {
            return null;
        }

        Fate? best = null;
        float bestScore = float.MinValue;

        foreach (Fate fate in fates)
        {
            if (!config.IsFateEnabledForIllegalMode(
                    fate.Id.Value,
                    zones.GetZone().IsPotFate(fate.Id.Value),
                    automatorConfig.PreferPotFates))
            {
                continue;
            }

            if (ShouldSkip(fate))
            {
                continue;
            }

            FateScore score = Score(fate);
            if (score.Value > bestScore)
            {
                bestScore = score.Value;
                best = fate;
            }
        }

        return best;
    }

    private bool ShouldSkip(Fate fate) =>
        config.ShouldSkipByProgress(fate.Progress)
        || (zones.GetZone().IsPotFate(fate.Id.Value)
            && potsConfig.ShouldSkipLivePot(fate.TimeRemainingSeconds));
}
