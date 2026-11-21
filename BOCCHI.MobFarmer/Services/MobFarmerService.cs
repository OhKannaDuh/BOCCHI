using BOCCHI.Common;
using BOCCHI.Common.Config;
using BOCCHI.Common.Data.Zones;
using BOCCHI.Common.Services;
using BOCCHI.MobFarmer.Data;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using Ocelot.Services.Logger;
using Ocelot.Extensions;
using Ocelot.Lifecycle;
using Ocelot.Services.Pathfinding;
using Ocelot.Services.PlayerState;
using Ocelot.Services.Translation;
using Ocelot.States;
using Ocelot.States.Flow;
using Ocelot.Windows;
using System.Numerics;

namespace BOCCHI.MobFarmer.Services;

public class MobFarmerService
(
    IMobScanner scanner,
    Func<IStateMachine<FarmerPhase>> stateMachineFactory,
    IPathfinder pathfinder,
    IPlayer player,
    IZoneProvider zones,
    IChatGui chat,
    ICondition conditions,
    UIConfig uiConfig,
    ITranslator<MainWindow> translator,
    IAutomationModeGuard modeGuard,
    IFarmerCombatController combat,
    FarmerSpotSession spots,
    ILogger<MobFarmerService> logger
) : IMobFarmer, IOnUpdate, IOnStop
{
    public int Order => 10;

    private FarmerPhase loggedPhase;

    private IStateMachine<FarmerPhase>? stateMachine;

    private bool aiPullRequested;

    private bool gatheringAiOn;

    private IStateMachine<FarmerPhase> StateMachine => stateMachine ??= stateMachineFactory();

    public bool Running { get; private set; }

    public bool Suspended { get; private set; }

    public FarmerYieldReason YieldReason { get; private set; }

    public Vector3 StartingPoint { get; private set; }

    public Vector3? StackPoint => spots.StackPoint;

    public string? CurrentSpotName => spots.Name;

    public int EffectiveMinimumMobsToStartFight => spots.EffectiveMinimumMobsToStartFight;

    public bool NeedsApproachSpot => spots.NeedsApproach;

    public void MarkArrivedAtSpot() => spots.MarkArrived();

    public FarmerPhase Phase => StateMachine.State;

    public bool CanAcceptYield
    {
        get
        {
            if (!Running || Suspended)
            {
                return false;
            }

            // Never yield mid-pull — the yielded mode doesn't fight, so we'd stand there and get hit.
            if (scanner.InCombat.Any() || conditions[ConditionFlag.InCombat])
            {
                return false;
            }

            if (Phase is FarmerPhase.Waiting or FarmerPhase.Buffing)
            {
                return true;
            }

            if (Phase != FarmerPhase.Gathering)
            {
                return false;
            }

            return !scanner.NotInCombat.Any(m =>
                player.Position.Distance2D(m.Position) <= FarmerPullAssist.PullRange);
        }
    }

    public void OnStop()
    {
        StopInternal();
    }

    public void Toggle()
    {
        if (Running)
        {
            StopInternal();
            return;
        }

        modeGuard.EnsureExclusive(AutomationMode.MobFarmer);

        Running = true;
        Suspended = false;
        YieldReason = FarmerYieldReason.None;
        if (StateMachine is FlowStateMachine<FarmerPhase> flow)
        {
            flow.Reset();
        }

        spots.Begin();
        StartingPoint = spots.Origin;
        combat.Prepare();
        loggedPhase = Phase;
        logger.Info("Mob Farmer started at {Spot}", spots.Name ?? "where you are standing");
    }

    public void SetSuspended(bool suspended, FarmerYieldReason reason = FarmerYieldReason.None)
    {
        if (!Running)
        {
            return;
        }

        if (Suspended == suspended && YieldReason == reason)
        {
            return;
        }

        Suspended = suspended;
        YieldReason = suspended ? reason : FarmerYieldReason.None;
        logger.Info(suspended ? "Mob Farmer paused for {Reason}" : "Mob Farmer resumed", reason);
        pathfinder.Stop();
        combat.Disable();

        if (suspended)
        {
            combat.Teardown();
            if (StateMachine is FlowStateMachine<FarmerPhase> flow)
            {
                flow.Reset();
            }

            return;
        }

        spots.RequireApproachIfAway();
        combat.Prepare();
    }

    public void Render()
    {
        if (!Running)
        {
            return;
        }

        StateMachine.Render();
    }

    public void Update()
    {
        scanner.Update();

        if (!Running)
        {
            return;
        }

        if (!zones.GetZone().IsOccultCrescentZone())
        {
            logger.Info("Left Occult Crescent — stopping Mob Farmer");
            StopInternal();
            BocchiChat.Print(chat, uiConfig, translator.T(".automation.mob_farmer.off_left_zone"));
            return;
        }

        if (Suspended)
        {
            return;
        }

        // Outside the fight the rotation only runs to tag a mob, or it spends cooldowns before the pack is stacked.
        if (Phase == FarmerPhase.Fighting)
        {
            gatheringAiOn = false;
            combat.Tick();
        }
        else if (aiPullRequested)
        {
            if (!gatheringAiOn)
            {
                logger.Debug("Mob Farmer: rotation on to tag an enemy");
            }

            gatheringAiOn = true;
            combat.EnableFighting();
            combat.Tick();
        }
        else if (gatheringAiOn)
        {
            gatheringAiOn = false;
            combat.Disable();
            logger.Debug("Mob Farmer: rotation off until the fight");
        }

        if (Phase == FarmerPhase.Waiting && spots.TickClaimed(scanner))
        {
            StartingPoint = spots.Origin;
            pathfinder.Stop();
            logger.Info("Mob Farmer: spot claimed by other players — moving to {Spot}", spots.Name ?? "the next spot");
        }

        aiPullRequested = false;
        StateMachine.Update();
        if (Phase != loggedPhase)
        {
            logger.Debug("Mob Farmer: {From} → {To}", loggedPhase, Phase);
            loggedPhase = Phase;
        }
    }

    public void RequestAiPull() => aiPullRequested = true;

    public bool CanAiPull => combat.IsActive;

    private void StopInternal()
    {
        if (Running)
        {
            logger.Info("Mob Farmer stopped");
        }

        Running = false;
        Suspended = false;
        YieldReason = FarmerYieldReason.None;
        spots.Reset();
        combat.Disable();
        combat.Teardown();
        if (StateMachine is FlowStateMachine<FarmerPhase> flowOff)
        {
            flowOff.Reset();
        }

        pathfinder.Stop();
    }
}
