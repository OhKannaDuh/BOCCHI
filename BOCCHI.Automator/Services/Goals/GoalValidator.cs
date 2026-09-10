using BOCCHI.Automator.Data;
using BOCCHI.Automator.Services;
using BOCCHI.Common.Config;
using BOCCHI.Common.Data.CriticalEncounters;
using BOCCHI.Common.Data.Fates;
using BOCCHI.Common.Data.Goals;
using BOCCHI.Common.Data.StateMemory;
using BOCCHI.Common.Data.Zones;
using BOCCHI.Common.Services;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using Ocelot.Services.Logger;

namespace BOCCHI.Automator.Services.Goals;

public class GoalValidator
(
    ICriticalEncounterRepository criticalEncounterRepository,
    ICriticalEncounterContext criticalEncounterContext,
    IFateRepository fateRepository,
    IFateContext fateContext,
    IZoneProvider zones,
    AutomatorConfig automatorConfig,
    FatesConfig fatesConfig,
    PotsConfig potsConfig,
    CriticalEncountersConfig criticalEncountersConfig,
    IPotCycleTracker potCycle,
    IAutomatorContext automatorContext,
    IAutomatorMemory memory,
    IFieldNoteTracker fieldNotes,
    IStartableCriticalEncounterFinder startableCriticalEncounters,
    ICondition conditions,
    IObjectTable objects,
    ILogger<GoalValidator> logger
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
            // Prefer pot FATEs: drop a CE you are still walking to when a live pot is up.
            if (!IsCommittedToCriticalEncounter(id)
                && automatorConfig.PreferPotFates
                && TryFindLiveAllowedPot(out Fate _))
            {
                logger.Debug(
                    "Invalidating CE {CeId} (still pathing) — Prefer pot FATEs, live pot up",
                    id.Value);
                return false;
            }

            return !automatorContext.IsCompletionist
                   || fieldNotes.ShouldPursueCriticalEncounter(id.Value);
        }

        if (!ce.IsActive())
        {
            return false;
        }

        // Keep the goal after we entered In CE / waited / still have EventId, tagged enemies, or the ring (#196).
        if (IsCommittedToCriticalEncounter(id))
        {
            return true;
        }

        logger.Debug(
            "Dropping CE {CeId} — no longer Preparing and not committed (was still pathing)",
            id.Value);
        return false;
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
        else if (isPot)
        {
            if (!fatesConfig.IsFateEnabledForIllegalMode(
                    id.Value,
                    isPotFate: true,
                    automatorConfig.PreferPotFates)
                || (!automatorConfig.ShouldDoFates
                    && !automatorConfig.ShouldFarmPotChests
                    && !automatorConfig.ShouldPrepositionToPots))
            {
                return false;
            }
        }
        else if (!automatorConfig.ShouldDoFates
                 || !fatesConfig.IsFateEnabledForIllegalMode(
                     id.Value,
                     isPot,
                     automatorConfig.PreferPotFates))
        {
            return false;
        }

        if (isPot && IsValidPotPreposition(id))
        {
            // Preposition is not a live pot — CEs still win (LeaveFateTravelForCeSeconds).
            if (!potsOnly
                && startableCriticalEncounters.FindStartable() is { } prepositionCe
                && ShouldLeaveFateTravelForCe(prepositionCe))
            {
                logger.Debug(
                    "Invalidating pot preposition {FateId} — startable CE {CeId} ({CeName})",
                    id.Value,
                    prepositionCe.Id.Value,
                    prepositionCe.Name);
                return false;
            }

            return PassesCompletionistFate(id.Value, potsOnly);
        }

        if (!fateRepository.HasFate(id))
        {
            return false;
        }

        Fate? live = fateRepository.Snapshot().FirstOrDefault(f => f.Id == id);
        bool registered = fateContext.GetFateId() == id;

        // Skip late FATEs while still pathing; once registered, finish (#174).
        if (!registered && live != null)
        {
            if (fatesConfig.ShouldSkipByProgress(live.Progress))
            {
                logger.Debug(
                    "Dropping FATE {FateId} — progress {Progress}% (skip at {Threshold}%)",
                    id.Value,
                    live.Progress,
                    fatesConfig.MaxFateProgressPercent);
                return false;
            }

            if (isPot && potsConfig.ShouldSkipLivePot(live.TimeRemainingSeconds))
            {
                logger.Debug(
                    "Dropping pot FATE {FateId} — {Minutes:F1}m left (skip threshold)",
                    id.Value,
                    live.TimeRemainingSeconds / 60.0);
                return false;
            }
        }

        // Live pot with Prefer (or pots-only): stay until despawn. Without Prefer, pots are
        // regular FATEs — a CE can take them while still traveling (#187).
        if (isPot)
        {
            if (potsOnly || automatorConfig.PreferPotFates)
            {
                return true;
            }

            if (!IsEngagedWithFate(id)
                && startableCriticalEncounters.FindStartable() is { } potCe
                && ShouldLeaveFateTravelForCe(potCe))
            {
                logger.Debug(
                    "Invalidating pot FATE {FateId} (still pathing) — startable CE {CeId} ({CeName}) with Prefer pot FATEs off",
                    id.Value,
                    potCe.Id.Value,
                    potCe.Name);
                return false;
            }

            return PassesCompletionistFate(id.Value, potsOnly);
        }

        // Live pot beats a non-pot FATE. If a CE is taking us now, CE wins instead.
        if (!potsOnly
            && !IsEngagedWithFate(id)
            && TryFindLiveAllowedPot(out Fate livePot)
            && !IsLeavingForStartableCe())
        {
            logger.Debug(
                "Invalidating FATE {FateId} (still pathing) — live pot {PotId}",
                id.Value,
                livePot.Id.Value);
            return false;
        }

        // Yield to a CE only while still traveling, and only when registration is almost up
        // (or the timer is unknown). Stay if registered or already fighting this FATE (#187).
        if (!potsOnly
            && !IsEngagedWithFate(id)
            && startableCriticalEncounters.FindStartable() is { } ce
            && ShouldLeaveFateTravelForCe(ce))
        {
            logger.Debug(
                "Invalidating FATE {FateId} (still pathing) — startable CE {CeId} ({CeName}) with {Remaining}s left (threshold {Threshold}s)",
                id.Value,
                ce.Id.Value,
                ce.Name,
                ce.GetTimeUntilStart() is { } remaining
                    ? Math.Max(0, (int)remaining.TotalSeconds)
                    : -1,
                automatorConfig.LeaveFateTravelForCeSeconds);
            return false;
        }

        if (!PassesCompletionistFate(id.Value, potsOnly))
        {
            return false;
        }

        // Pot cutoff only blocks starting a FATE — keep one you are already in.
        if (IsEngagedWithFate(id))
        {
            return true;
        }

        PotCycleSnapshot cycle = potCycle.Snapshot;
        bool potFarming = fatesConfig.IsPotFallbackGatingEnabled(
            (uint)cycle.PredictedNextPotFateId,
            automatorConfig.ShouldDoFates,
            automatorConfig.PreferPotFates,
            automatorConfig.ShouldFarmPotChests,
            automatorConfig.ShouldPrepositionToPots);
        TimeSpan cutoff = TimeSpan.FromMinutes(Math.Max(0, potsConfig.FateFallbackCutoffMinutes));
        PotFallbackStartDecision decision = PotFallbackWindow.Evaluate(
            cycle,
            DateTimeOffset.UtcNow,
            cutoff,
            potFarming,
            "FATE");
        return decision.AllowStart;
    }

    /// <summary>
    ///     0 = always leave FATE travel for a startable CE. Otherwise only when registration has
    ///     this many seconds (or fewer) left, or the timer cannot be read.
    /// </summary>
    private bool ShouldLeaveFateTravelForCe(CriticalEncounter ce)
    {
        int threshold = automatorConfig.LeaveFateTravelForCeSeconds;
        if (threshold <= 0)
        {
            return true;
        }

        if (ce.GetTimeUntilStart() is not { } remaining)
        {
            return true;
        }

        return remaining <= TimeSpan.FromSeconds(threshold);
    }

    private bool IsLeavingForStartableCe() =>
        startableCriticalEncounters.FindStartable() is { } ce
        && ShouldLeaveFateTravelForCe(ce);

    /// <summary>
    ///     Registered in the FATE, or already fighting its mobs (rim pull before CurrentFate).
    ///     Do not abort for a CE / pot in those cases.
    /// </summary>
    private bool IsEngagedWithFate(FateId id) =>
        fateContext.GetFateId() == id
        || (conditions[ConditionFlag.InCombat] && fateContext.IsInCombatWith(id))
        || (memory.TryRemember<SuspendTravelForActivityMemory>(out SuspendTravelForActivityMemory _)
            && memory.TryRemember<GoalMemory>(out GoalMemory goal)
            && goal.Goal.GoalType is FateGoal(var goalId)
            && goalId == id);

    private bool IsCommittedToCriticalEncounter(CriticalEncounterId id)
    {
        if (memory.TryRemember<CommittedCriticalEncounterMemory>(out CommittedCriticalEncounterMemory committed)
            && committed.IsFor(id))
        {
            return true;
        }

        if (criticalEncounterContext.GetCriticalEncounterId() == id)
        {
            return true;
        }

        if (memory.TryRemember<WaitingForCriticalEncounterMemory>(out WaitingForCriticalEncounterMemory wait)
            && wait.IsFor(id))
        {
            return true;
        }

        return IsSuspendedInCriticalEncounter(id);
    }

    /// <summary>
    ///     In CE travel latch only counts with EventId, CE-tagged enemies, or still inside the wait ring.
    /// </summary>
    private bool IsSuspendedInCriticalEncounter(CriticalEncounterId id)
    {
        if (!memory.TryRemember<SuspendTravelForActivityMemory>(out SuspendTravelForActivityMemory _))
        {
            return false;
        }

        if (criticalEncounterContext.GetCriticalEncounterId() == id
            || criticalEncounterContext.HasEncounterEnemies(id))
        {
            return true;
        }

        CriticalEncounter? ce = criticalEncounterRepository.SnapshotWithoutForkedTower()
            .FirstOrDefault(c => c.Id == id);
        if (ce is not { } encounter || !encounter.IsActive() || objects.LocalPlayer is not { } player)
        {
            return false;
        }

        float combatRadius = NavigationConstants.CriticalEncounterRedRadius(
            encounter.Radius,
            encounter.AreaShape);
        return NavigationConstants.IsInsideCriticalEncounterWaitArea(
            encounter.RegistrationCenter,
            combatRadius,
            encounter.AreaShape,
            player.Position);
    }

    private bool PassesCompletionistFate(uint fateId, bool potsOnly) =>
        potsOnly
        || !automatorContext.IsCompletionist
        || fieldNotes.ShouldPursueFate(fateId);

    /// <summary>
    ///     Predicted pot goal kept before the FATE exists (and briefly after predicted spawn).
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
                automatorConfig.ShouldFarmPotChests,
                automatorConfig.ShouldPrepositionToPots))
        {
            return false;
        }

        // Drop if prediction is stale (spawn never observed).
        if (DateTimeOffset.UtcNow > cycle.PredictedNextSpawnAt + PotCycleTracker.PredictionStaleGrace)
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
            potsConfig.PotSpawnLeadMinutes,
            true);
    }

    private bool TryFindLiveAllowedPot(out Fate pot)
    {
        Fate? live = LivePotPriority.FindStartable(
            fateRepository,
            zones,
            automatorConfig,
            fatesConfig,
            potsConfig,
            automatorContext,
            fieldNotes);
        if (live == null)
        {
            pot = null!;
            return false;
        }

        pot = live;
        return true;
    }
}
