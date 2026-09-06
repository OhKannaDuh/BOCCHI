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
    UIConfig uiConfig,
    AutoRotationController autoRotation,
    IAutomationModeGuard modeGuard,
    Func<ITreasureHunter> hunterFactory,
    ITranslator<MainWindow> translator,
    ILogger<Automator> logger
) : IAutomator, IOnUpdate, IOnStop
{
    // Before IllegalModeTreasureFillerService (0) so pot-chest farm latches first.
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

        // Keep GoalMemory; Treasure Hunt owns vnav while suspended.
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

        // Drop in-flight buff approach — crystal pathing at camp fought the antiquarian (#203).
        memory.Forget<ApplyingBuffsMemory>();
        memory.Forget<ManualBuffRunMemory>();
        memory.Forget<InquiringMindAttemptedMemory>();
        memory.Forget<BuffSupportJobMemory>();

        // Keep GoalMemory and FATE/CE commitment — only drop the active path steps so
        // shopping owns vnav. Forgetting SuspendTravel / Committed* mid-CE used to make
        // GoalValidator drop the encounter as "still pathing" on resume.
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

    /// <summary>
    ///     Cache Me If You Can being up means there are chests to open, so that — not goal
    ///     bookkeeping — is what starts the farm. Latching off the goal transition alone raced the
    ///     treasure filler: whichever ran first in the frame either started the farm or latched a
    ///     Sight survey, and the survey path Returns at High priority and picks a new FATE.
    ///     Runs every tick; cheap, and a no-op once a farm is latched or the buff is gone.
    ///     The buff does not say which pot it came from — see
    ///     <see cref="ResolvePotFateForActiveBuff"/>.
    /// </summary>
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

        // GoalMemory kept — Update() will rebuild GoalPathStepMemory from here.
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
            // GoalMemory kept — Update() replans once the path map is ready.
        }

        // Kick load immediately so the UI shows Loading/Building instead of waiting for a FATE pick.
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
        if (!IsActive)
        {
            return;
        }

        // Zone lock even while suspended for treasure — leaving OC must fully turn the mode off.
        if (!zones.GetZone().IsOccultCrescentZone())
        {
            DisableDueToLeavingOccultCrescent();
            return;
        }

        if (SuspendedForTreasure || SuspendedForShopping)
        {
            return;
        }

        autoRotation.Tick();

        // Mid-route cancel (vnav stop / emergency) — don't replan until mode is toggled.
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
                if (goal.Goal.GoalType is FateGoal fateGoal)
                {
                    TryStartPotChestFarm(fateGoal.id);
                }

                string goalLabel = goal.Goal.GoalType switch
                {
                    FateGoal(var id) => $"FATE {id.Value}",
                    CriticalEncounterGoal(var id) => $"CE {id.Value}",
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
                     && !memory.TryRemember<WaitingForPotFateMemory>(out WaitingForPotFateMemory _)
                     && !memory.TryRemember<SuspendTravelForActivityMemory>(out SuspendTravelForActivityMemory _)
                     && !memory.TryRemember<CommittedCriticalEncounterMemory>(out CommittedCriticalEncounterMemory _)
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

        // Still mid-FATE (e.g. HasFate flicker) — wait until the pot is actually gone.
        if (fates.HasFate(fateId))
        {
            if (!memory.TryRemember<PendingPotChestFarmMemory>(out PendingPotChestFarmMemory _))
            {
                memory.TryAdd(new PendingPotChestFarmMemory(fateId));
                // Stop next-goal travel immediately so we don't Return/TP before chests.
                IllegalModeActivityWork.ForgetTravelLatches(memory);
                SoftStopPathfinding();
                logger.Debug("Pot FATE {FateId} still active — deferring chest farm", fateId.Value);
            }

            return;
        }

        memory.Forget<PendingPotChestFarmMemory>();

        // Magical Elixir + compass hints whenever we have pot chest data (SH authored groups, NH binned).
        // WaitingForBuff waits for Cache Me; leftover elixir alone must not start a blind sweep.
        ActivityData? potFate = zone.GetPotFateData().FirstOrDefault(f => f.Id == fateId.Value);
        if (potFate != null && PotTreasureFilter.CanRunSmart(zone, fateId.Value))
        {
            logger.Info("Starting pot treasure (elixir/hints) for fate {FateId}", fateId.Value);
            BeginExclusivePotChestFarm(PotChestFarmMemory.CreateSmart(fateId));
            return;
        }

        // Blind authored sweep only when the buff is already present (no WaitingForBuff phase).
        if (objects.LocalPlayer?.StatusList.Has(PotTreasureIds.TreasureBuffStatusId) != true)
        {
            logger.Debug(
                "Skipping blind pot chest farm for fate {FateId}: no Cache Me If You Can buff and no smart groups",
                fateId.Value);
            return;
        }

        if (!zone.GetPotChestData().TryGetValue(fateId.Value, out List<PotChestData>? chestData))
        {
            return;
        }

        List<Vector3> positions = chestData.Select(chest => chest.Position).ToList();
        if (context.IsPotsAndTreasure || potsConfig.ShouldFarmRerollPotChests)
        {
            positions.AddRange(zone.GetRerollPotChestData().Select(chest => chest.Position));
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

    /// <summary>
    ///     Cache Me does not name the pot it came from. Nearest FATE centre is wrong after a
    ///     manual elixir — you are often standing on a distant chest, closer to the other pot.
    /// </summary>
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
        foreach (KeyValuePair<int, List<PotChestData>> group in zone.GetPotChestData())
        {
            foreach (PotChestData chest in group.Value)
            {
                float dist = playerPos.Distance2D(chest.Position);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    bestFate = group.Key;
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

    /// <summary>
    /// Drop next-goal / Return travel so FarmingPotChests can open reveals.
    /// Otherwise Choosing during Pending + Pathfinding (High) preempts the farm.
    /// </summary>
    private void BeginExclusivePotChestFarm(PotChestFarmMemory farm)
    {
        memory.Forget<GoalMemory>();
        IllegalModeActivityWork.ForgetTravelLatches(memory);
        memory.Forget<ReturningStateMemory>();
        SoftStopPathfinding();

        // Drop post-activity Sight / map latch so Return-to-camp cannot beat Magical Elixir.
        if (memory.TryRemember(out AutomaticTreasureSurveyMemory survey))
        {
            survey.PendingSurvey = false;
            survey.WaitingForSurveyResult = false;
            survey.PendingMapHunt = false;
            survey.SurveyWaitDeadlineUtc = DateTime.MinValue;
        }

        memory.TryAdd(farm);
    }
}
