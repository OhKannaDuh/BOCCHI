using BOCCHI.Automator.Data;
using BOCCHI.Common.Config;
using BOCCHI.Common.Data.Fates;
using BOCCHI.Common.Data.Zones;
using BOCCHI.Common.Services;

namespace BOCCHI.Automator.Services.Goals;

/// <summary>
///     Live FATE / pot pick shared by Choosing Activity and hunt yield.
/// </summary>
internal static class LivePotPriority
{
    public static bool IsStartable(
        Fate fate,
        IZone zone,
        AutomatorConfig automatorConfig,
        FatesConfig fatesConfig,
        PotsConfig potsConfig,
        IAutomatorContext automatorContext,
        IFieldNoteTracker fieldNotes)
    {
        if (!zone.IsPotFate(fate.Id.Value))
        {
            return false;
        }

        if (automatorContext.IsPotsAndTreasure)
        {
            return true;
        }

        if (!fatesConfig.IsFateEnabledForIllegalMode(
                fate.Id.Value,
                isPotFate: true,
                automatorConfig.PreferPotFates))
        {
            return false;
        }

        if (!automatorConfig.ShouldDoFates
            && !automatorConfig.ShouldFarmPotChests
            && !automatorConfig.ShouldPrepositionToPots)
        {
            return false;
        }

        if (automatorContext.IsCompletionist && !fieldNotes.ShouldPursueFate(fate.Id.Value))
        {
            return false;
        }

        return !fatesConfig.ShouldSkipByProgress(fate.Progress)
               && !potsConfig.ShouldSkipLivePot(fate.TimeRemainingSeconds);
    }

    /// <summary>Any live pot Illegal Mode would start (not scored against other FATEs).</summary>
    public static Fate? FindStartable(
        IFateRepository fateRepository,
        IZoneProvider zones,
        AutomatorConfig automatorConfig,
        FatesConfig fatesConfig,
        PotsConfig potsConfig,
        IAutomatorContext automatorContext,
        IFieldNoteTracker fieldNotes)
    {
        IZone zone = zones.GetZone();
        return fateRepository.Snapshot()
            .FirstOrDefault(fate => IsStartable(
                fate,
                zone,
                automatorConfig,
                fatesConfig,
                potsConfig,
                automatorContext,
                fieldNotes));
    }

    /// <summary>Best live pot, else best other FATE. No pot preposition.</summary>
    public static Fate? FindBest(
        IReadOnlyList<Fate> snapshot,
        IZone zone,
        IFateScorer fateScorer,
        IPotCycleTracker potCycle,
        AutomatorConfig automatorConfig,
        FatesConfig fatesConfig,
        PotsConfig potsConfig,
        IAutomatorContext automatorContext,
        IFieldNoteTracker fieldNotes)
    {
        if (snapshot.Count == 0)
        {
            return null;
        }

        bool potsOnly = automatorContext.IsPotsAndTreasure;
        if (!potsOnly
            && !automatorConfig.ShouldDoFates
            && !automatorConfig.ShouldFarmPotChests
            && !automatorConfig.ShouldPrepositionToPots)
        {
            return null;
        }

        Fate? bestPot = null;
        Fate? bestOther = null;
        float bestPotScore = float.MinValue;
        float bestOtherScore = float.MinValue;
        DateTimeOffset now = DateTimeOffset.UtcNow;
        PotCycleSnapshot cycle = potCycle.Snapshot;
        bool potFarming = potsOnly
            || fatesConfig.IsPotFallbackGatingEnabled(
                (uint)cycle.PredictedNextPotFateId,
                automatorConfig.ShouldDoFates,
                automatorConfig.PreferPotFates,
                automatorConfig.ShouldFarmPotChests,
                automatorConfig.ShouldPrepositionToPots);

        foreach (Fate fate in snapshot)
        {
            bool isPot = zone.IsPotFate(fate.Id.Value);
            if (isPot)
            {
                if (!IsStartable(
                        fate,
                        zone,
                        automatorConfig,
                        fatesConfig,
                        potsConfig,
                        automatorContext,
                        fieldNotes))
                {
                    continue;
                }
            }
            else if (potsOnly || !automatorConfig.ShouldDoFates)
            {
                continue;
            }
            else if (!fatesConfig.IsFateEnabledForIllegalMode(
                         fate.Id.Value,
                         isPotFate: false,
                         automatorConfig.PreferPotFates))
            {
                continue;
            }
            else if (!potsOnly
                     && automatorContext.IsCompletionist
                     && !fieldNotes.ShouldPursueFate(fate.Id.Value))
            {
                continue;
            }
            else
            {
                TimeSpan cutoff = TimeSpan.FromMinutes(Math.Max(0, potsConfig.FateFallbackCutoffMinutes));
                PotFallbackStartDecision decision = PotFallbackWindow.Evaluate(
                    cycle,
                    now,
                    cutoff,
                    potFarming,
                    "FATE");
                if (!decision.AllowStart)
                {
                    continue;
                }
            }

            float scoreValue = potsOnly && isPot
                ? Math.Max(1f, fateScorer.Score(fate).Value)
                : fateScorer.Score(fate).Value;
            if (scoreValue <= 0f)
            {
                continue;
            }

            if (isPot)
            {
                if (scoreValue > bestPotScore)
                {
                    bestPotScore = scoreValue;
                    bestPot = fate;
                }
            }
            else if (scoreValue > bestOtherScore)
            {
                bestOtherScore = scoreValue;
                bestOther = fate;
            }
        }

        return bestPot ?? bestOther;
    }
}
