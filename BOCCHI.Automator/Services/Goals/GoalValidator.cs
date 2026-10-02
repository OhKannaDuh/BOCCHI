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
    IForkedTowerRegistration forkedTower,
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
            ForkedTowerGoal(var id) => ValidateForkedTower(id),
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
            if (!IsCommittedToCriticalEncounter(id) && IsLeavingForForkedTower())
            {
                logger.Debug("Invalidating CE {CeId} (still pathing) — Forked Tower registration is open", id.Value);
                return false;
            }

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
            if (IsLeavingForForkedTower())
            {
                logger.Debug("Invalidating pot preposition {FateId} — Forked Tower registration is open", id.Value);
                return false;
            }

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

        if (!IsEngagedWithFate(id) && IsLeavingForForkedTower())
        {
            logger.Debug("Invalidating FATE {FateId} (still pathing) — Forked Tower registration is open", id.Value);
            return false;
        }

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

    private bool ValidateForkedTower(CriticalEncounterId id)
    {
        if (!forkedTower.IsEnabled || forkedTower.IsInsideTower())
        {
            return false;
        }

        if (criticalEncounterRepository.TryGetForkedTower() is not { } tower
            || tower.Id != id
            || !forkedTower.CanStillEnter(tower))
        {
            return false;
        }

        if (tower.IsPreparing())
        {
            return true;
        }

        if (!tower.IsActive()
            || !memory.TryRemember<WaitingForForkedTowerMemory>(out WaitingForForkedTowerMemory wait)
            || !wait.IsFor(id))
        {
            return false;
        }

        // The teleport lands a moment after Battle starts; give it time before going back to farming.
        wait.BattleSeenAt ??= DateTimeOffset.UtcNow;
        if (DateTimeOffset.UtcNow - wait.BattleSeenAt.Value < ForkedTowerTeleportGrace)
        {
            return true;
        }

        logger.Info("Forked Tower {Id} started without teleporting us in — resuming", id.Value);
        return false;
    }

    private static readonly TimeSpan ForkedTowerTeleportGrace = TimeSpan.FromSeconds(20);

    private bool IsLeavingForForkedTower() => forkedTower.FindRegistrable() != null;

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

    private bool IsEngagedWithFate(FateId id) =>
        fateContext.GetFateId() == id
        || (conditions[ConditionFlag.InCombat] && fateContext.IsInCombatWith(id))
        || (memory.TryRemember<CommittedFateMemory>(out CommittedFateMemory committed) && committed.IsFor(id))
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

        if (DateTimeOffset.UtcNow > cycle.PredictedNextSpawnAt + PotCycleTracker.PredictionStaleGrace)
        {
            return false;
        }

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
