using BOCCHI.Automator.Data;
using BOCCHI.Automator.Services;
using BOCCHI.Automator.Services.Goals;
using BOCCHI.Buff.Services;
using BOCCHI.Common.Config;
using BOCCHI.Common.Data.CriticalEncounters;
using BOCCHI.Common.Data.Fates;
using BOCCHI.Common.Data.Goals;
using BOCCHI.Common.Data.StateMemory;
using BOCCHI.Common.Data.Zones;
using BOCCHI.Common.Services;
using ECommons.Throttlers;
using Ocelot.Services.Logger;
using Ocelot.States.Score;

namespace BOCCHI.Automator.StateMachine.Handlers;

public class ChoosingActivityHandler
(
    IAutomatorMemory memory,
    IAutomatorContext automatorContext,
    IFateRepository fateRepository,
    IGoalFactory goalFactory,
    IBuffProvider buffs,
    BuffConfig buffConfig,
    AutomatorConfig automatorConfig,
    FatesConfig fatesConfig,
    PotsConfig potsConfig,
    IFateScorer fateScorer,
    IPotCycleTracker potCycle,
    IZoneProvider zones,
    IFieldNoteTracker fieldNotes,
    IStartableCriticalEncounterFinder startableCriticalEncounters,
    IForkedTowerRegistration forkedTower,
    ILogger<ChoosingActivityHandler> logger
) : ScoreStateHandler<AutomatorState, StatePriority>(AutomatorState.ChoosingActivity)
{
    private bool PotsOnly => automatorContext.IsPotsAndTreasure;

    public override StatePriority GetScore()
    {
        if (memory.TryRemember<GoalMemory>(out GoalMemory current))
        {
            return Blocked($"goal {current.Goal.Describe()}");
        }

        if (memory.TryRemember<NavigationInterruptedMemory>(out NavigationInterruptedMemory _))
        {
            return Blocked("navigation interrupted");
        }

        if (buffConfig.ShouldAutomateBuffs
            && buffs.ShouldRefreshAny()
            && zones.GetZone().GetNearbyKnowledgeCrystals().Any())
        {
            return Blocked("buff refresh pending");
        }

        if (memory.TryRemember<ApplyingBuffsMemory>(out ApplyingBuffsMemory _))
        {
            return Blocked("applying buffs");
        }

        if (memory.TryRemember<PotChestFarmMemory>(out PotChestFarmMemory _)
            || memory.TryRemember<PendingPotChestFarmMemory>(out PendingPotChestFarmMemory _))
        {
            return Blocked("pot chest farm");
        }

        if (TriageSession.IsActive(memory))
        {
            return Blocked("triage");
        }

        if (memory.TryRemember<AutomaticTreasureSurveyMemory>(out AutomaticTreasureSurveyMemory survey)
            && survey.IsBusy)
        {
            return Blocked("treasure survey");
        }

        bool hasCriticalEncounter = !PotsOnly && startableCriticalEncounters.FindStartable() != null;
        if (forkedTower.FindRegistrable() == null
            && !hasCriticalEncounter
            && FindStartableFate() == null
            && !CanPrepositionToPot(out _))
        {
            return StatePriority.Never;
        }

        return StatePriority.Low;
    }

    private StatePriority Blocked(string reason)
    {
        if (EzThrottler.Throttle("ChoosingActivity::ForkedTowerBlocked", 15000)
            && forkedTower.FindRegistrable() is { } tower)
        {
            logger.Info("Forked Tower {Id} registration open but not choosing: {Reason}", tower.Id.Value, reason);
        }

        return StatePriority.Never;
    }

    public override void Handle()
    {
        if (forkedTower.FindRegistrable() is { } tower)
        {
            memory.TryAdd(new GoalMemory(goalFactory.ForkedTower(tower.Id)));
            logger.Info("Chose Forked Tower {Id} ({Name}) — registration open", tower.Id.Value, tower.Name);
            return;
        }

        if (!PotsOnly)
        {
            CriticalEncounter? criticalEncounter = startableCriticalEncounters.FindStartable();
            if (criticalEncounter != null)
            {
                IGoal goal = goalFactory.CriticalEncounter(criticalEncounter.Id);
                memory.TryAdd(new GoalMemory(goal));
                logger.Info("Chose CE {Id} ({Name})", criticalEncounter.Id.Value, criticalEncounter.Name);
                return;
            }
        }

        Fate? fate = FindStartableFate();
        if (fate != null)
        {
            IGoal goal = goalFactory.Fate(fate.Id);
            memory.TryAdd(new GoalMemory(goal));
            logger.Info("Chose FATE {Id} ({Name})", fate.Id.Value, fate.Name);
            return;
        }

        TryChoosePotPreposition();
    }

    private Fate? FindStartableFate()
    {
        FateId? exclude = null;
        RouteUnreachableGoalMemory? unreachable = IllegalModeActivityWork.TakeActiveUnreachable(memory);
        if (unreachable?.Goal.GoalType is FateGoal(var fateId))
        {
            exclude = fateId;
        }

        return LivePotPriority.FindBest(
            fateRepository.Snapshot(),
            zones.GetZone(),
            fateScorer,
            potCycle,
            automatorConfig,
            fatesConfig,
            potsConfig,
            automatorContext,
            fieldNotes,
            exclude);
    }

    private bool TryChoosePotPreposition()
    {
        if (!CanPrepositionToPot(out FateId potId))
        {
            return false;
        }

        IGoal goal = goalFactory.Fate(potId);
        memory.TryAdd(new GoalMemory(goal));
        logger.Info("Prepositioning to pot FATE {Id} before spawn", potId.Value);
        return true;
    }

    private string? lastPrepositionSkip;

    private bool CanPrepositionToPot(out FateId potId)
    {
        potId = default;

        if (!PotsOnly && !automatorConfig.ShouldPrepositionToPots)
        {
            return SkipPreposition("\"Wait near pots before they spawn\" is off");
        }

        PotCycleSnapshot cycle = potCycle.Snapshot;
        if (!cycle.HasPredictedNextPot)
        {
            return SkipPreposition("no pot spawn predicted yet");
        }

        if (!PotsOnly && !fatesConfig.IsPotFallbackGatingEnabled(
                (uint)cycle.PredictedNextPotFateId,
                automatorConfig.ShouldDoFates,
                automatorConfig.PreferPotFates,
                automatorConfig.ShouldFarmPotChests,
                automatorConfig.ShouldPrepositionToPots))
        {
            return SkipPreposition(
                !automatorConfig.ShouldDoFates
                    ? "\"Do FATEs\" is off"
                    : $"pot FATE {cycle.PredictedNextPotFateId} is not an allowed FATE "
                      + "(tick it under Allowed FATEs, or turn on \"Prefer pot FATEs\")");
        }

        if (CompletionistBlocksFate((uint)cycle.PredictedNextPotFateId))
        {
            return SkipPreposition($"Completionist has nothing left to log on pot FATE {cycle.PredictedNextPotFateId}");
        }

        FateId predicted = new((ushort)cycle.PredictedNextPotFateId);
        RouteUnreachableGoalMemory? unreachable = IllegalModeActivityWork.TakeActiveUnreachable(memory);
        if (unreachable?.MatchesFate(predicted) == true)
        {
            return SkipPreposition(
                $"route to pot FATE {predicted.Value} failed recently — waiting before retry");
        }

        if (fateRepository.HasFate(predicted))
        {
            return SkipPreposition("pot FATE is already live — going to it, not prepositioning");
        }

        if (!PotFallbackWindow.ShouldPreposition(
                cycle,
                DateTimeOffset.UtcNow,
                potsConfig.PotSpawnLeadMinutes,
                potFarmingEnabled: true))
        {
            TimeSpan untilSpawn = cycle.PredictedNextSpawnAt - DateTimeOffset.UtcNow;
            return SkipPreposition(
                untilSpawn <= TimeSpan.Zero
                    ? "pot prediction is stale (spawn never observed)"
                    : $"outside the leave-early window — next pot in {untilSpawn.TotalMinutes:0}m "
                      + $"(lead {potsConfig.PotSpawnLeadMinutes}m)");
        }

        lastPrepositionSkip = null;
        potId = predicted;
        return true;
    }

    private bool SkipPreposition(string reason)
    {
        if (lastPrepositionSkip != reason)
        {
            lastPrepositionSkip = reason;
            logger.Debug("Not prepositioning to pot: {Reason}", reason);
        }

        return false;
    }

    private bool CompletionistBlocksFate(uint fateId) =>
        !PotsOnly
        && automatorContext.IsCompletionist
        && !fieldNotes.ShouldPursueFate(fateId);
}
