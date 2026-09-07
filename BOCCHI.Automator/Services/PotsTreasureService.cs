using BOCCHI.Automator.Data;
using BOCCHI.Automator.Services.PotTreasure;
using BOCCHI.Common;
using BOCCHI.Common.Config;
using BOCCHI.Common.Data.Fates;
using BOCCHI.Common.Data.Goals;
using BOCCHI.Common.Data.StateMemory;
using BOCCHI.Common.Data.Zones;
using BOCCHI.Common.Services;
using BOCCHI.Treasure.Services;
using Dalamud.Plugin.Services;
using Ocelot.Extensions;
using Ocelot.Lifecycle;
using Ocelot.Services.Logger;
using Ocelot.Services.Translation;
using Ocelot.Windows;

namespace BOCCHI.Automator.Services;

/// <summary>
/// Dedicated pots + treasure mode: pot FATEs and chests, then treasure hunt
/// until the configured pot lead before the next pot spawn; preposition and repeat.
/// </summary>
public class PotsTreasureService
(
    IAutomator automator,
    IAutomatorContext context,
    IAutomatorMemory memory,
    ITreasureHunter hunter,
    IPotCycleTracker potCycle,
    IFateRepository fates,
    IZoneProvider zones,
    IGoalFactory goalFactory,
    IAutomationModeGuard modeGuard,
    IObjectTable objects,
    IChatGui chat,
    UIConfig uiConfig,
    PotsConfig potsConfig,
    ITranslator<MainWindow> translator,
    ILogger<PotsTreasureService> logger
) : IPotsTreasureMode, IOnUpdate, IOnStop
{
    private static readonly TimeSpan TreasureRestartDelay = TimeSpan.FromMinutes(3);

    private static readonly TimeSpan ManualTreasureOverride = TimeSpan.FromMinutes(5);

    private readonly HashSet<uint> visitedTreasureNodes = [];

    private DateTimeOffset nextTreasureHuntAt = DateTimeOffset.MinValue;

    private DateTimeOffset forceTreasureUntil = DateTimeOffset.MinValue;

    private bool huntWasRunning;

    /// <summary>Live pot FATE id we already handed to the automator this pot window.</summary>
    private uint ensuredPotFateId;

    public bool Running => context.IsPotsAndTreasure;

    public bool Paused { get; private set; }

    public bool ManagedByMobFarmer { get; private set; }

    public PotsTreasurePhase Phase { get; private set; } = PotsTreasurePhase.Off;

    public void OnStop()
    {
        if (Running)
        {
            StopHuntSession();
            automator.TogglePotsAndTreasure();
        }

        ResetTreasureLoop();
        Paused = false;
        Phase = PotsTreasurePhase.Off;
        ManagedByMobFarmer = false;
    }

    public void Toggle()
    {
        if (Running)
        {
            StopHuntSession();
            automator.TogglePotsAndTreasure();
            ResetTreasureLoop();
            Paused = false;
            Phase = PotsTreasurePhase.Off;
            ManagedByMobFarmer = false;
            return;
        }

        if (!hunter.IsVnavAvailable)
        {
            BocchiChat.PrintError(chat, uiConfig, translator.T(".automation.pots_treasure.requires_vnav"));
            return;
        }

        modeGuard.EnsureExclusive(AutomationMode.PotsAndTreasure);

        // Fresh hunt session for this mode (location-resume still applies if coffers remain).
        if (hunter.Running)
        {
            hunter.Toggle();
        }

        automator.TogglePotsAndTreasure();
        hunter.ManagedByPotsTreasure = true;
        ResetTreasureLoop();
        Paused = false;
        Phase = PotsTreasurePhase.DoingPots;
        ManagedByMobFarmer = false;
        logger.Info("Pots & Treasure mode started");
    }

    public bool StartManagedFromFarmer()
    {
        if (Running)
        {
            ManagedByMobFarmer = true;
            Phase = PotsTreasurePhase.DoingPots;
            return true;
        }

        if (!hunter.IsVnavAvailable)
        {
            BocchiChat.PrintError(chat, uiConfig, translator.T(".automation.pots_treasure.requires_vnav"));
            return false;
        }

        ManagedByMobFarmer = true;
        automator.TogglePotsAndTreasure();
        hunter.ManagedByPotsTreasure = false;
        ResetTreasureLoop();
        Paused = false;
        Phase = PotsTreasurePhase.DoingPots;
        logger.Debug("Pots & Treasure: managed pot window for Mob Farmer");
        return Running;
    }

    public void StopManagedFromFarmer()
    {
        if (!ManagedByMobFarmer)
        {
            return;
        }

        ManagedByMobFarmer = false;
        if (Running)
        {
            StopHuntSession();
            automator.TogglePotsAndTreasure();
        }

        ResetTreasureLoop();
        Paused = false;
        Phase = PotsTreasurePhase.Off;
    }

    public void Pause()
    {
        if (!Running || Paused)
        {
            return;
        }

        Paused = true;
        SoftPauseMovement();
        logger.Info("Pots & Treasure paused");
    }

    public void Resume()
    {
        if (!Running || !Paused)
        {
            return;
        }

        Paused = false;
        logger.Info("Pots & Treasure resumed");
    }

    public void ResumeTreasureHunt()
    {
        if (!Running)
        {
            return;
        }

        Paused = false;
        forceTreasureUntil = DateTimeOffset.UtcNow + ManualTreasureOverride;
        nextTreasureHuntAt = DateTimeOffset.MinValue;
        EnterHuntPhase();
        logger.Info("Pots & Treasure: manually resumed treasure hunt");
    }

    public void Update()
    {
        if (!Running)
        {
            if (Phase != PotsTreasurePhase.Off || hunter.ManagedByPotsTreasure || ManagedByMobFarmer)
            {
                StopHuntSession();
                ResetTreasureLoop();
                Paused = false;
                Phase = PotsTreasurePhase.Off;
                ManagedByMobFarmer = false;
            }

            return;
        }

        // Leave OC: Automator.Update also turns the mode off; do not Toggle here (race flips it back on).
        if (!zones.GetZone().IsOccultCrescentZone())
        {
            SoftPauseMovement();
            return;
        }

        if (automator.SuspendedForShopping)
        {
            SoftPauseMovement();
            return;
        }

        if (!ManagedByMobFarmer)
        {
            hunter.ManagedByPotsTreasure = true;
            CaptureFinishedTreasureHunt();
        }

        if (Paused)
        {
            SoftPauseMovement();
            return;
        }

        if (ManagedByMobFarmer)
        {
            if (NeedsPotWork())
            {
                EnterPotPhase();
                return;
            }

            StopManagedFromFarmer();
            return;
        }

        bool needPots = NeedsPotWork();
        if (needPots)
        {
            EnterPotPhase();
        }
        else
        {
            EnterHuntPhase();
        }
    }

    private void SoftPauseMovement()
    {
        if (hunter.Running && !hunter.Paused)
        {
            hunter.Pause();
        }

        automator.SoftStopPathfinding();
    }

    private void EnterPotPhase()
    {
        bool leavingHunt = Phase != PotsTreasurePhase.DoingPots || automator.SuspendedForTreasure;
        Phase = PotsTreasurePhase.DoingPots;
        automator.SetSuspendedForTreasure(false);

        if (hunter.Running && !hunter.Paused)
        {
            hunter.Pause();
            logger.Debug("Pots & Treasure: paused hunt for pot window");
        }

        // Hunt filler freezes the automator; when a pot pops we must hand it a FATE goal
        // (ChoosingActivity alone can stay blocked by interrupt latches / empty score paths).
        if (leavingHunt)
        {
            memory.Forget<NavigationInterruptedMemory>();
            IllegalModeActivityWork.ForgetTravelLatches(memory);
            automator.SoftStopPathfinding();
            ensuredPotFateId = 0;
        }

        EnsureLivePotFateGoal();
    }

    private void EnsureLivePotFateGoal()
    {
        IZone zone = zones.GetZone();
        Fate? pot = fates.Snapshot().FirstOrDefault(f => zone.IsPotFate(f.Id.Value));
        if (pot == null)
        {
            ensuredPotFateId = 0;
            return;
        }

        if (memory.TryRemember<GoalMemory>(out GoalMemory existing)
            && existing.Goal.GoalType is FateGoal fateGoal
            && fateGoal.id.Value == pot.Id.Value)
        {
            ensuredPotFateId = pot.Id.Value;
            return;
        }

        if (ensuredPotFateId == pot.Id.Value
            && memory.TryRemember<GoalMemory>(out GoalMemory _))
        {
            return;
        }

        memory.Forget<GoalMemory>();
        memory.Forget<GoalPathStepMemory>();
        memory.Forget<NavigationInterruptedMemory>();
        memory.TryAdd(new GoalMemory(goalFactory.Fate(pot.Id)));
        ensuredPotFateId = pot.Id.Value;
        logger.Info(
            "Pots & Treasure: targeting live pot FATE {Id} ({Name})",
            pot.Id.Value,
            pot.Name);
    }

    private void EnterHuntPhase()
    {
        Phase = PotsTreasurePhase.Hunting;
        ensuredPotFateId = 0;
        automator.SetSuspendedForTreasure(true);

        if (!hunter.IsVnavReady)
        {
            return;
        }

        if (!hunter.Running)
        {
            if (DateTimeOffset.UtcNow < nextTreasureHuntAt)
            {
                return;
            }

            hunter.ConfigureManagedRun(visitedTreasureNodes);
            hunter.StartManaged();
            huntWasRunning = hunter.Running;
            logger.Debug("Pots & Treasure: started treasure hunt filler");
            return;
        }

        if (hunter.Paused)
        {
            hunter.Resume();
            huntWasRunning = true;
            // Rebuild the walk from the current authored step after pot vnav ownership.
            hunter.RecalculateRoute();
            logger.Debug("Pots & Treasure: resumed treasure hunt where it left off");
        }
    }

    private void StopHuntSession()
    {
        automator.SetSuspendedForTreasure(false);
        hunter.ManagedByPotsTreasure = false;
        if (hunter.Running)
        {
            hunter.Toggle();
        }
    }

    private bool NeedsPotWork()
    {
        if (DateTimeOffset.UtcNow < forceTreasureUntil)
        {
            return false;
        }

        if (memory.TryRemember<PotChestFarmMemory>(out PotChestFarmMemory _))
        {
            return true;
        }

        if (memory.TryRemember<PendingPotChestFarmMemory>(out PendingPotChestFarmMemory _))
        {
            return true;
        }

        if (objects.LocalPlayer?.StatusList.Has(PotTreasureIds.TreasureBuffStatusId) == true)
        {
            return true;
        }

        if (memory.TryRemember<WaitingForPotFateMemory>(out WaitingForPotFateMemory _))
        {
            return true;
        }

        if (memory.TryRemember<GoalMemory>(out GoalMemory goal)
            && goal.Goal.GoalType is FateGoal fateGoal
            && zones.GetZone().IsPotFate(fateGoal.id.Value))
        {
            return true;
        }

        IZone zone = zones.GetZone();
        if (fates.Snapshot().Any(f => zone.IsPotFate(f.Id.Value)))
        {
            return true;
        }

        PotCycleSnapshot cycle = potCycle.Snapshot;
        if (cycle.CurrentActivePotFateId != 0)
        {
            return true;
        }

        if (!cycle.HasPredictedNextPot)
        {
            return false;
        }

        return PotFallbackWindow.ShouldPreposition(
            cycle,
            DateTimeOffset.UtcNow,
            potsConfig.PotSpawnLeadMinutes,
            potFarmingEnabled: true);
    }

    private void CaptureFinishedTreasureHunt()
    {
        if (!huntWasRunning || hunter.Running)
        {
            huntWasRunning = hunter.Running;
            return;
        }

        IReadOnlySet<uint> checkedNodes = hunter.LastCompletedRunNodeIds;
        if (checkedNodes.Count > 0)
        {
            foreach (uint nodeId in checkedNodes)
            {
                visitedTreasureNodes.Add(nodeId);
            }

            nextTreasureHuntAt = DateTimeOffset.UtcNow + TreasureRestartDelay;
            logger.Info(
                "Pots & Treasure: treasure hunt completed {CheckedCount} nodes; {VisitedCount} visited this session. Restart after {Delay:mm\\:ss}.",
                checkedNodes.Count,
                visitedTreasureNodes.Count,
                TreasureRestartDelay);
        }
        else
        {
            nextTreasureHuntAt = DateTimeOffset.UtcNow + TreasureRestartDelay;
            logger.Info(
                "Pots & Treasure: treasure hunt stopped with no completed nodes; restart after {Delay:mm\\:ss}.",
                TreasureRestartDelay);
        }

        huntWasRunning = false;
    }

    private void ResetTreasureLoop()
    {
        visitedTreasureNodes.Clear();
        nextTreasureHuntAt = DateTimeOffset.MinValue;
        forceTreasureUntil = DateTimeOffset.MinValue;
        huntWasRunning = false;
        ensuredPotFateId = 0;
    }
}
