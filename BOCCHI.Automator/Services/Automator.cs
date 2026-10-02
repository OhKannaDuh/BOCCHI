using BOCCHI.Automator.Data;
using BOCCHI.Automator.Services.Goals;
using BOCCHI.Automator.Services.PotTreasure;
using BOCCHI.Common;
using BOCCHI.Common.Config;
using BOCCHI.Common.Data.Aethernet;
using BOCCHI.Common.Data.Fates;
using BOCCHI.Common.Data.Goals;
using BOCCHI.Common.Data.StateMemory;
using BOCCHI.Common.Data.Zones;
using BOCCHI.Common.Data.Zones.Graph;
using BOCCHI.Common.Services;
using BOCCHI.Common.Services.Paths;
using BOCCHI.Treasure.Services;
using Dalamud.Plugin.Services;
using Ocelot.Chain;
using Ocelot.Extensions;
using Ocelot.Ipc.Lifestream;
using Ocelot.Ipc.VNavmesh;
using Ocelot.Lifecycle;
using Ocelot.Services.Logger;
using Ocelot.Services.Pathfinding;
using Ocelot.Services.Translation;
using Ocelot.States;
using Ocelot.Windows;
using System.Numerics;

namespace BOCCHI.Automator.Services;

public class Automator
(
    IAutomatorMemory memory,
    Func<IStateMachine<AutomatorState>> stateMachineFactory,
    IPathCalculator calculator,
    IGoalValidator validator,
    IAutomatorContext context,
    IChainManager manager,
    IPathfinder pathfinder,
    IVNavmeshIpc vnav,
    ILifestreamIpc lifestream,
    IZoneProvider zones,
    IFateRepository fates,
    IPotCycleTracker potCycle,
    IObjectTable objects,
    IChatGui chat,
    PotsConfig potsConfig,
    AutomatorConfig automatorConfig,
    ForkedTowerConfig forkedTowerConfig,
    IForkedTowerRegistration forkedTower,
    UIConfig uiConfig,
    AutoRotationController autoRotation,
    IAutomationModeGuard modeGuard,
    Func<ITreasureHunter> hunterFactory,
    PotChestLocationSyncService potChests,
    ITranslator<MainWindow> translator,
    ILogger<Automator> logger
) : IAutomator, IOnUpdate, IOnStop
{
    public int Order => 5;

    private IStateMachine<AutomatorState>? stateMachine;

    private IStateMachine<AutomatorState> StateMachine => stateMachine ??= stateMachineFactory();

    public bool Enabled => context.IsIllegalMode;

    public bool IsIllegalMode => context.IsIllegalMode;

    public bool IsActive => context.Enabled;

    public bool IsPotsAndTreasure => context.IsPotsAndTreasure;

    public bool IsCompletionist => context.IsCompletionist;

    public bool SuspendedForTreasure { get; private set; }

    public bool SuspendedForShopping { get; private set; }

    public AutomatorState? CurrentState =>
        IsActive && !SuspendedForTreasure && !SuspendedForShopping ? StateMachine.State : null;

    private AutomatorState? lastLoggedState;

    private bool wasInsideForkedTower;

    public void OnStop() => StopAutomation();

    public void SetSuspendedForTreasure(bool suspended)
    {
        if (SuspendedForTreasure == suspended)
        {
            return;
        }

        logger.Debug(
            "Illegal Mode suspend for treasure hunt: {Was} → {Now} (state was {State})",
            SuspendedForTreasure,
            suspended,
            CurrentState?.ToString() ?? "null");
        SuspendedForTreasure = suspended;
        if (!suspended)
        {
            return;
        }

        IllegalModeActivityWork.ForgetTravelLatches(memory);
        SoftStopPathfinding();
        autoRotation.DisableAi();
    }

    public void SetSuspendedForShopping(bool suspended)
    {
        if (SuspendedForShopping == suspended)
        {
            return;
        }

        logger.Debug(
            "Illegal Mode suspend for shopping: {Was} → {Now} (state was {State})",
            SuspendedForShopping,
            suspended,
            CurrentState?.ToString() ?? "null");
        SuspendedForShopping = suspended;
        if (!suspended)
        {
            return;
        }

        memory.Forget<ApplyingBuffsMemory>();
        memory.Forget<ManualBuffRunMemory>();
        memory.Forget<InquiringMindAttemptedMemory>();
        memory.Forget<BuffSupportJobMemory>();

        memory.Forget<GoalPathStepMemory>();
        SoftStopPathfinding();
        autoRotation.DisableAi();
    }

    public void SoftStopPathfinding()
    {
        PathStepSoftStop.Stop(manager, pathfinder, vnav);
        AethernetTeleport.AbortIfBusy(lifestream);
    }

    public void Toggle()
    {
        bool turningOn = !context.IsIllegalMode;
        if (turningOn)
        {
            modeGuard.EnsureExclusive(AutomationMode.IllegalMode);
        }

        AutomatorRunMode target = turningOn ? AutomatorRunMode.IllegalMode : AutomatorRunMode.Off;
        if (context.RunMode == target)
        {
            return;
        }

        context.SetRunMode(target);
        BocchiChat.Print(chat, uiConfig, translator.T(Enabled ? ".automation.automator.illegal_mode_on" : ".automation.automator.illegal_mode_off"));
        ApplyRunModeSideEffects(turningOn);
    }

    public void TogglePotsAndTreasure()
    {
        bool turningOn = !context.IsPotsAndTreasure;
        if (turningOn && (context.IsIllegalMode || context.IsCompletionist))
        {
            StopAutomation();
        }

        AutomatorRunMode target = turningOn ? AutomatorRunMode.PotsAndTreasure : AutomatorRunMode.Off;
        if (context.RunMode == target)
        {
            return;
        }

        context.SetRunMode(target);
        BocchiChat.Print(chat, uiConfig, translator.T(turningOn
            ? ".automation.pots_treasure.on"
            : ".automation.pots_treasure.off"));
        ApplyRunModeSideEffects(turningOn);
    }

    public void ToggleCompletionist()
    {
        bool turningOn = !context.IsCompletionist;
        if (turningOn)
        {
            modeGuard.EnsureExclusive(AutomationMode.Completionist);
        }

        AutomatorRunMode target = turningOn ? AutomatorRunMode.Completionist : AutomatorRunMode.Off;
        if (context.RunMode == target)
        {
            return;
        }

        context.SetRunMode(target);
        BocchiChat.Print(chat, uiConfig, translator.T(turningOn
            ? ".completionist.mode_on"
            : ".completionist.mode_off"));
        ApplyRunModeSideEffects(turningOn);
    }

    private void ApplyRunModeSideEffects(bool turningOn)
    {
        if (!turningOn)
        {
            SuspendedForTreasure = false;
            SuspendedForShopping = false;
            StopAutomation();
            return;
        }

        ITreasureHunter hunter = hunterFactory();
        SuspendedForTreasure = hunter.Running && hunter.ManagedByIllegalModeFiller;
        SuspendedForShopping = false;

        memory.Forget<NavigationInterruptedMemory>();
        IllegalModeActivityWork.ForgetJobRestoreMemories(memory);
        autoRotation.PrepareForIllegalMode();
        EnsurePotChestFarmForBuff();
    }

    private void EnsurePotChestFarmForBuff()
    {
        if (memory.TryRemember<PotChestFarmMemory>(out PotChestFarmMemory _)
            || memory.TryRemember<PendingPotChestFarmMemory>(out PendingPotChestFarmMemory _))
        {
            return;
        }

        if (objects.LocalPlayer is not { } player
            || !player.StatusList.Has(PotTreasureIds.TreasureBuffStatusId))
        {
            return;
        }

        IZone zone = zones.GetZone();
        ActivityData? source = ResolvePotFateForActiveBuff(zone, player.Position);
        if (source == null)
        {
            return;
        }

        logger.Info(
            "Cache Me If You Can is up — farming pot chests for fate {FateId}",
            source.Id);
        TryStartPotChestFarm(new FateId((ushort)source.Id));
    }

    public void RefreshPathfinding()
    {
        if (!IsActive || SuspendedForTreasure || SuspendedForShopping)
        {
            return;
        }

        logger.Debug("Refreshing pathfinding from current position");
        memory.Forget<NavigationInterruptedMemory>();
        IllegalModeActivityWork.ForgetTravelLatches(memory, includePotChests: true);
        SoftStopPathfinding();

        if (!memory.TryRemember<GoalMemory>(out GoalMemory _))
        {
            BocchiChat.Print(chat, uiConfig, translator.T(".automation.automator.pathfinding_refreshed_no_goal"));
            return;
        }

        BocchiChat.Print(chat, uiConfig, translator.T(".automation.automator.pathfinding_refreshed"));
    }

    public void RebuildPathMap()
    {
        IZone zone = zones.GetZone();
        if (!zone.IsOccultCrescentZone())
        {
            BocchiChat.Print(chat, uiConfig, translator.T(".automation.automator.path_map_rebuild_wrong_zone"));
            return;
        }

        logger.Info("Rebuilding zone path map for territory {Territory}", zone.TerritoryType);
        zone.InvalidateGraph("manual rebuild");

        if (IsActive)
        {
            memory.Forget<NavigationInterruptedMemory>();
            IllegalModeActivityWork.ForgetTravelLatches(memory, includePotChests: true);
            SoftStopPathfinding();
            memory.Forget<GoalPathStepMemory>();
        }

        _ = zone.GetGraph().ContinueWith(
            task =>
            {
                if (task.IsFaulted)
                {
                    logger.Warning(task.Exception, "Path map rebuild failed");
                }
                else
                {
                    logger.Info(
                        "Path map ready for territory {Territory} ({Source})",
                        zone.TerritoryType,
                        zone.GraphSource);
                }
            },
            TaskScheduler.Default);

        BocchiChat.Print(chat, uiConfig, translator.T(".automation.automator.path_map_rebuilding"));
    }

    public void Render()
    {
        if (!IsActive || SuspendedForTreasure || SuspendedForShopping)
        {
            return;
        }

        StateMachine.Render();
    }

    public void Update()
    {
        // Tracked while off too, so starting Illegal Mode by hand inside the tower is not undone.
        bool insideTower = forkedTower.IsInsideTower();
        bool justEnteredTower = insideTower && !wasInsideForkedTower;
        wasInsideForkedTower = insideTower;

        if (!IsActive)
        {
            return;
        }

        if (!zones.GetZone().IsOccultCrescentZone())
        {
            DisableDueToLeavingOccultCrescent();
            return;
        }

        if (justEnteredTower && forkedTowerConfig.AutoRegisterInIllegalMode && context.IsIllegalMode)
        {
            DisableDueToEnteringForkedTower();
            return;
        }

        if (SuspendedForShopping)
        {
            return;
        }

        if (SuspendedForTreasure)
        {
            TryStartPendingPotChestFarm();
            EnsurePotChestFarmForBuff();
            if (!memory.TryRemember<PotChestFarmMemory>(out PotChestFarmMemory _))
            {
                return;
            }

            ITreasureHunter hunt = hunterFactory();
            if (hunt.Running && !hunt.Paused)
            {
                hunt.Pause();
                logger.Debug("Paused treasure hunt — pot chest farm latched while Illegal Mode was suspended");
            }

            SetSuspendedForTreasure(false);
        }

        autoRotation.Tick();

        if (memory.TryRemember<NavigationInterruptedMemory>(out NavigationInterruptedMemory _))
        {
            StateMachine.Update();
            LogStateTransitionIfNeeded();
            return;
        }

        TryStartPendingPotChestFarm();
        EnsurePotChestFarmForBuff();

        if (memory.TryRemember<GoalMemory>(out GoalMemory goal))
        {
            if (!validator.Validate(goal.Goal))
            {
                // Only if we actually took part — a pot FATE dropped en route (progress skip) has no
                // Cache Me to wait for, and idling for it parks us among high-level mobs.
                if (goal.Goal.GoalType is FateGoal fateGoal
                    && memory.TryRemember<CommittedFateMemory>(out CommittedFateMemory committed)
                    && committed.IsFor(fateGoal.id))
                {
                    TryStartPotChestFarm(fateGoal.id);
                }

                string goalLabel = goal.Goal.GoalType switch
                {
                    FateGoal(var id) => $"FATE {id.Value}",
                    CriticalEncounterGoal(var id) => $"CE {id.Value}",
                    ForkedTowerGoal(var id) => $"Forked Tower {id.Value}",
                    _ => goal.Goal.Describe(),
                };
                logger.Debug(
                    "Goal no longer valid ({Goal}) — aborting pathfinding",
                    goalLabel);
                memory.Forget<GoalMemory>();
                IllegalModeActivityWork.ForgetTravelLatches(memory);
                SoftStopPathfinding();
            }
            else if (!memory.TryRemember<GoalPathStepMemory>(out GoalPathStepMemory _)
                     && !memory.TryRemember<WaitingForCriticalEncounterMemory>(out WaitingForCriticalEncounterMemory _)
                     && !memory.TryRemember<WaitingForForkedTowerMemory>(out WaitingForForkedTowerMemory _)
                     && !memory.TryRemember<WaitingForPotFateMemory>(out WaitingForPotFateMemory _)
                     && !memory.TryRemember<SuspendTravelForActivityMemory>(out SuspendTravelForActivityMemory _)
                     && !memory.TryRemember<CommittedCriticalEncounterMemory>(out CommittedCriticalEncounterMemory _)
                     && !memory.TryRemember<CommittedFateMemory>(out CommittedFateMemory _)
                     && !memory.TryRemember<ApplyingBuffsMemory>(out ApplyingBuffsMemory _))
            {
                memory.TryAdd(new GoalPathStepMemory(goal.Goal, calculator, automatorConfig.StopAfterReturn));
            }
        }

        StateMachine.Update();
        LogStateTransitionIfNeeded();
    }

    private void LogStateTransitionIfNeeded()
    {
        AutomatorState? now = StateMachine.State;
        if (now == lastLoggedState)
        {
            return;
        }

        logger.Debug(
            "Illegal Mode state {Prev} → {Next}",
            lastLoggedState?.ToString() ?? "null",
            now?.ToString() ?? "null");
        lastLoggedState = now;
    }

    private void DisableDueToLeavingOccultCrescent()
    {
        string offMessage = context.RunMode switch
        {
            AutomatorRunMode.PotsAndTreasure => ".automation.pots_treasure.off_left_zone",
            AutomatorRunMode.Completionist => ".completionist.mode_off_left_zone",
            _ => ".automation.automator.illegal_mode_off_left_zone",
        };

        logger.Info("Left Occult Crescent — turning off {Mode}", context.RunMode);

        context.SetRunMode(AutomatorRunMode.Off);
        BocchiChat.Print(chat, uiConfig, translator.T(offMessage));
        ApplyRunModeSideEffects(turningOn: false);
    }

    // BossMod takes over inside the tower; Illegal Mode would only fight it.
    private void DisableDueToEnteringForkedTower()
    {
        logger.Info("Entered the Forked Tower — turning off Illegal Mode");

        context.SetRunMode(AutomatorRunMode.Off);
        BocchiChat.Print(chat, uiConfig, translator.T(".automation.automator.illegal_mode_off_forked_tower"));
        ApplyRunModeSideEffects(turningOn: false);
    }

    private void StopAutomation()
    {
        SuspendedForTreasure = false;
        SuspendedForShopping = false;
        memory.Wipe();
        manager.CancelAll();
        AethernetTeleport.AbortIfBusy(lifestream);
        pathfinder.Stop();
        vnav.Stop();
        autoRotation.TeardownForIllegalMode();

        if (stateMachine != null)
        {
            StateMachine.Reset();
        }

        lastLoggedState = null;
    }

    private void TryStartPendingPotChestFarm()
    {
        if (!memory.TryRemember<PendingPotChestFarmMemory>(out PendingPotChestFarmMemory pending))
        {
            return;
        }

        if (fates.HasFate(pending.FateId))
        {
            return;
        }

        memory.Forget<PendingPotChestFarmMemory>();
        TryStartPotChestFarm(pending.FateId);
    }

    private void TryStartPotChestFarm(FateId fateId)
    {
        bool farmChests = automatorConfig.ShouldFarmPotChests || context.IsPotsAndTreasure;
        if (!farmChests || memory.TryRemember<PotChestFarmMemory>(out PotChestFarmMemory _))
        {
            return;
        }

        IZone zone = zones.GetZone();
        if (!zone.IsPotFate(fateId.Value))
        {
            return;
        }

        if (fates.HasFate(fateId))
        {
            if (!memory.TryRemember<PendingPotChestFarmMemory>(out PendingPotChestFarmMemory _))
            {
                memory.TryAdd(new PendingPotChestFarmMemory(fateId));
                IllegalModeActivityWork.ForgetTravelLatches(memory);
                SoftStopPathfinding();
                logger.Debug("Pot FATE {FateId} still active — deferring chest farm", fateId.Value);
            }

            return;
        }

        memory.Forget<PendingPotChestFarmMemory>();

        potChests.EnsureFreshForFarm();
        ActivityData? potFate = zone.GetPotFateData().FirstOrDefault(f => f.Id == fateId.Value);
        IReadOnlyList<PotChestData> primaryPads = potChests.GetPrimaryPads(zone, fateId.Value);
        if (potFate != null && zone.IsPotFate(fateId.Value) && PotTreasureFilter.CanRunSmart(primaryPads))
        {
            logger.Info("Starting pot treasure (elixir/hints) for fate {FateId}", fateId.Value);
            BeginExclusivePotChestFarm(PotChestFarmMemory.CreateSmart(fateId));
            return;
        }

        if (objects.LocalPlayer?.StatusList.Has(PotTreasureIds.TreasureBuffStatusId) != true)
        {
            logger.Debug(
                "Skipping blind pot chest farm for fate {FateId}: no Cache Me If You Can buff and no smart groups",
                fateId.Value);
            return;
        }

        if (primaryPads.Count == 0)
        {
            return;
        }

        List<Vector3> positions = primaryPads.Select(chest => chest.Position).ToList();
        if (context.IsPotsAndTreasure || potsConfig.ShouldFarmRerollPotChests)
        {
            positions.AddRange(potChests.GetRerollPads(zone).Select(chest => chest.Position));
        }

        if (objects.LocalPlayer is not { } player)
        {
            return;
        }

        positions = positions
            .OrderBy(position => player.Position.Distance(position))
            .ToList();

        if (positions.Count == 0)
        {
            return;
        }

        logger.Info("Starting pot chest farm for fate {FateId} with {Count} chest positions", fateId.Value, positions.Count);
        BeginExclusivePotChestFarm(PotChestFarmMemory.CreateBlind(fateId, positions));
    }

    private ActivityData? ResolvePotFateForActiveBuff(IZone zone, Vector3 playerPos)
    {
        List<ActivityData> pots = zone.GetPotFateData();
        if (pots.Count == 0)
        {
            return null;
        }

        Fate? live = fates.Snapshot().FirstOrDefault(f => zone.IsPotFate(f.Id.Value));
        if (live != null)
        {
            return pots.FirstOrDefault(p => p.Id == live.Id.Value);
        }

        PotCycleSnapshot cycle = potCycle.Snapshot;
        if (cycle.HasKnownAnchor
            && cycle.CurrentActivePotFateId == 0
            && cycle.AnchorPotFateId != 0
            && DateTimeOffset.UtcNow - cycle.AnchorSpawnAt < TimeSpan.FromMinutes(12))
        {
            ActivityData? anchored = pots.FirstOrDefault(p => p.Id == cycle.AnchorPotFateId);
            if (anchored != null)
            {
                return anchored;
            }
        }

        int? bestFate = null;
        float bestDist = float.MaxValue;
        foreach (ActivityData pot in pots)
        {
            foreach (PotChestData chest in potChests.GetPrimaryPads(zone, pot.Id))
            {
                float dist = playerPos.Distance2D(chest.Position);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    bestFate = pot.Id;
                }
            }
        }

        if (bestFate is int fateId)
        {
            ActivityData? byPad = pots.FirstOrDefault(p => p.Id == fateId);
            if (byPad != null)
            {
                return byPad;
            }
        }

        return pots
            .OrderBy(p => Vector3.DistanceSquared(p.Position, playerPos))
            .FirstOrDefault();
    }

    private void BeginExclusivePotChestFarm(PotChestFarmMemory farm)
    {
        memory.Forget<GoalMemory>();
        IllegalModeActivityWork.ForgetTravelLatches(memory);
        memory.Forget<ReturningStateMemory>();
        SoftStopPathfinding();

        if (memory.TryRemember(out AutomaticTreasureSurveyMemory survey))
        {
            survey.PendingSurvey = false;
            survey.WaitingForSurveyResult = false;
            survey.PendingMapHunt = false;
            survey.SurveyWaitDeadlineUtc = DateTime.MinValue;
        }

        ITreasureHunter hunt = hunterFactory();
        if (hunt.Running && !hunt.Paused)
        {
            hunt.Pause();
            logger.Debug("Paused treasure hunt — beginning exclusive pot chest farm");
        }

        if (SuspendedForTreasure)
        {
            SetSuspendedForTreasure(false);
        }

        memory.TryAdd(farm);
    }
}
