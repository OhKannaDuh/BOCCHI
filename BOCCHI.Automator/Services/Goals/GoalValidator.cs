using BOCCHI.Automator.Data;
using BOCCHI.Automator.Services;
using BOCCHI.Common.Config;
using BOCCHI.Common.Data.CriticalEncounters;
using BOCCHI.Common.Data.Fates;
using BOCCHI.Common.Data.Goals;
using BOCCHI.Common.Data.StateMemory;
using BOCCHI.Common.Data.Zones;
using BOCCHI.Common.Services;

namespace BOCCHI.Automator.Services.Goals;

public class GoalValidator
(
    ICriticalEncounterRepository criticalEncounterRepository,
    ICriticalEncounterContext criticalEncounterContext,
    IFateRepository fateRepository,
    IZoneProvider zones,
    AutomatorConfig automatorConfig,
    FatesConfig fatesConfig,
    PotsConfig potsConfig,
    CriticalEncountersConfig criticalEncountersConfig,
    IPotCycleTracker potCycle,
    IAutomatorContext automatorContext,
    IAutomatorMemory memory
) : IGoalValidator
{
    public bool Validate(IGoal goal)
    {
        return goal.GoalType switch
        {
            CriticalEncounterGoal(var id) => ValidateCriticalEncounter(id),
            FateGoal(var id) => ValidateFate(id),
            var _ => throw new ArgumentOutOfRangeException(nameof(GoalType))
        };
    }

    private bool ValidateCriticalEncounter(CriticalEncounterId id)
    {
        if (automatorContext.IsPotsAndTreasure)
        {
            return false;
        }

        if (!automatorConfig.ShouldDoCriticalEncounters
            || !criticalEncountersConfig.IsCriticalEncounterEnabled(id.Value))
        {
            return false;
        }

        CriticalEncounter? ce = criticalEncounterRepository.SnapshotWithoutForkedTower()
            .FirstOrDefault(c => c.Id == id);
        if (ce == null)
        {
            return false;
        }

        if (ce.IsPreparing())
        {
            return true;
        }

        if (!ce.IsActive())
        {
            return false;
        }

        // During Battle, prefer the player's CE event id. If we had already reached the CE wait
        // area before it started, keep the goal so we do not path out to another activity.
        if (criticalEncounterContext.GetCriticalEncounterId() == id)
        {
            return true;
        }

        if (!memory.TryRemember<WaitingForCriticalEncounterMemory>(out WaitingForCriticalEncounterMemory wait)
            || !wait.IsFor(id))
        {
            return false;
        }

        wait.MarkBattleStarted();
        return true;
    }

    private bool ValidateFate(FateId id)
    {
        bool isPot = zones.GetZone().IsPotFate(id.Value);
        bool potsOnly = automatorContext.IsPotsAndTreasure;

        if (potsOnly)
        {
            if (!isPot)
            {
                return false;
            }
        }
        else if (!automatorConfig.ShouldDoFates || !fatesConfig.IsFateEnabled(id.Value))
        {
            return false;
        }

        if (isPot && IsValidPotPreposition(id))
        {
            return true;
        }

        if (!fateRepository.HasFate(id))
        {
            return false;
        }

        if (!isPot)
        {
            PotCycleSnapshot cycle = potCycle.Snapshot;
            bool potFarming = fatesConfig.IsPotFallbackGatingEnabled(
                (uint)cycle.PredictedNextPotFateId,
                automatorConfig.ShouldDoFates,
                automatorConfig.PreferPotFates,
                automatorConfig.ShouldFarmPotChests);
            (TimeSpan cutoff, int lead) = GetIllegalPotWindow();
            PotFallbackStartDecision decision = PotFallbackWindow.Evaluate(
                cycle,
                DateTimeOffset.UtcNow,
                cutoff,
                lead,
                potFarming,
                "FATE");
            if (!decision.AllowStart)
            {
                return false;
            }
        }

        int minRemaining = potsOnly
            ? PotsTreasureDefaults.MinPotFateMinutesRemaining
            : potsConfig.MinPotFateMinutesRemaining;
        if (minRemaining <= 0 || !isPot)
        {
            return true;
        }

        Fate? fate = fateRepository.Snapshot().FirstOrDefault(f => f.Id.Value == id.Value);
        if (fate == null)
        {
            return false;
        }

        // Drop pot FATE goals that are about to expire so we don't path into an empty event.
        return fate.TimeRemainingSeconds >= minRemaining * 60L;
    }

    /// <summary>
    ///     Predicted pot goal kept before the FATE exists (and briefly after predicted spawn) (#112).
    /// </summary>
    private bool IsValidPotPreposition(FateId id)
    {
        bool potsOnly = automatorContext.IsPotsAndTreasure;
        if (!potsOnly && !automatorConfig.ShouldPrepositionToPots)
        {
            return false;
        }

        PotCycleSnapshot cycle = potCycle.Snapshot;
        if (cycle.PredictedNextPotFateId != id.Value)
        {
            return false;
        }

        if (!potsOnly && !fatesConfig.IsPotFallbackGatingEnabled(
                (uint)cycle.PredictedNextPotFateId,
                automatorConfig.ShouldDoFates,
                automatorConfig.PreferPotFates,
                automatorConfig.ShouldFarmPotChests))
        {
            return false;
        }

        // Drop if prediction is stale (spawn never observed).
        if (DateTimeOffset.UtcNow > cycle.PredictedNextSpawnAt + TimeSpan.FromMinutes(5))
        {
            return false;
        }

        // Once the FATE is up, normal HasFate validation takes over.
        if (fateRepository.HasFate(id))
        {
            return false;
        }

        return PotFallbackWindow.ShouldPreposition(
            cycle,
            DateTimeOffset.UtcNow,
            potsOnly ? TimeSpan.Zero : TimeSpan.FromMinutes(Math.Max(0, potsConfig.FateFallbackCutoffMinutes)),
            potsOnly ? PotsTreasureDefaults.PrepositionLeadMinutes : potsConfig.PotSpawnLeadMinutes,
            true);
    }

    private (TimeSpan Cutoff, int Lead) GetIllegalPotWindow() =>
    (
        TimeSpan.FromMinutes(Math.Max(0, potsConfig.FateFallbackCutoffMinutes)),
        potsConfig.PotSpawnLeadMinutes
    );
}
