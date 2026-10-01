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

public class StartableCriticalEncounterFinder
(
    IAutomatorContext automatorContext,
    AutomatorConfig automatorConfig,
    FatesConfig fatesConfig,
    PotsConfig potsConfig,
    CriticalEncountersConfig criticalEncountersConfig,
    ICriticalEncounterRepository criticalEncounterRepository,
    IPotCycleTracker potCycle,
    IFateRepository fateRepository,
    IZoneProvider zones,
    IFieldNoteTracker fieldNotes,
    IAutomatorMemory memory
) : IStartableCriticalEncounterFinder
{
    public CriticalEncounter? FindStartable()
    {
        if (automatorContext.IsPotsAndTreasure || !automatorConfig.ShouldDoCriticalEncounters)
        {
            return null;
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        PotCycleSnapshot cycle = potCycle.Snapshot;
        bool potFarming = fatesConfig.IsPotFallbackGatingEnabled(
            (uint)cycle.PredictedNextPotFateId,
            automatorConfig.ShouldDoFates,
            automatorConfig.PreferPotFates,
            automatorConfig.ShouldFarmPotChests,
            automatorConfig.ShouldPrepositionToPots);

        RouteUnreachableGoalMemory? unreachable = IllegalModeActivityWork.TakeActiveUnreachable(memory);
        FateId? excludeFate = unreachable?.Goal.GoalType is FateGoal(var skipped) ? skipped : null;

        if (automatorConfig.PreferPotFates
            && LivePotPriority.FindStartable(
                fateRepository,
                zones,
                automatorConfig,
                fatesConfig,
                potsConfig,
                automatorContext,
                fieldNotes,
                excludeFate) != null)
        {
            return null;
        }

        foreach (CriticalEncounter ce in criticalEncounterRepository.SnapshotWithoutForkedTower())
        {
            if (!ce.IsPreparing() || !criticalEncountersConfig.IsCriticalEncounterEnabled(ce.Id.Value))
            {
                continue;
            }

            if (unreachable?.MatchesCriticalEncounter(ce.Id) == true)
            {
                continue;
            }

            if (automatorContext.IsCompletionist && !fieldNotes.ShouldPursueCriticalEncounter(ce.Id.Value))
            {
                continue;
            }

            PotFallbackStartDecision decision = PotFallbackWindow.Evaluate(
                cycle,
                now,
                TimeSpan.FromMinutes(Math.Max(0, potsConfig.CeFallbackCutoffMinutes)),
                potFarming,
                "CE");
            if (!decision.AllowStart)
            {
                continue;
            }

            return ce;
        }

        return null;
    }
}
