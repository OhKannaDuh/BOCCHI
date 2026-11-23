using BOCCHI.Common.Config;
using BOCCHI.Common.Data.Zones;
using BOCCHI.Common.Services;
using BOCCHI.MobFarmer.Data;
using BOCCHI.MobFarmer.Services;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using Ocelot.Extensions;
using Ocelot.Services.Logger;
using Ocelot.Services.Pathfinding;
using Ocelot.Services.PlayerState;
using Ocelot.States.Flow;
using System.Numerics;

namespace BOCCHI.MobFarmer.StateMachine.Handlers;

public class WaitingHandler
(
    MobFarmerConfig config,
    MovementConfig movementConfig,
    IMobFarmer farmer,
    IMobScanner scanner,
    ICondition conditions,
    IObjectTable objects,
    IPathfinder pathfinder,
    IZoneProvider zones,
    IPlayer player,
    IActivityNavigation navigation,
    ILogger<WaitingHandler> logger
) : FlowStateHandler<FarmerPhase>(FarmerPhase.Waiting)
{
    private const float ArriveRange = 8f;

    private const float PathArriveRange = 2f;

    private const ulong HomeWatchKey = 0;

    private readonly FarmerWalkStuckAssist stuckAssist = new();

    // Beyond this a plain walk loses to Return / aethernet; same planner as Completionist survey points.
    private const float TravelDistance = 150f;

    private DateTime? readySinceUtc;

    private bool travelStarted;

    private bool loggedLowHp;

    public override void Exit(FarmerPhase next)
    {
        if (travelStarted && navigation.IsBusy)
        {
            navigation.Cancel();
        }

        travelStarted = false;
        stuckAssist.Reset();
        readySinceUtc = null;
        base.Exit(next);
    }

    public override FarmerPhase? Handle()
    {
        if (scanner.InCombat.Any() || conditions[ConditionFlag.InCombat])
        {
            return FarmerPhase.Fighting;
        }

        float homeDistance = player.Position.Distance2D(farmer.StartingPoint);
        if (farmer.NeedsApproachSpot)
        {
            if (homeDistance <= ArriveRange)
            {
                farmer.MarkArrivedAtSpot();
                stuckAssist.Reset();
                travelStarted = false;
            }
            else
            {
                if (travelStarted && navigation.IsBusy)
                {
                    return null;
                }

                if (!travelStarted && homeDistance > TravelDistance && navigation.CanPathfind)
                {
                    travelStarted = true;
                    logger.Info("Mob Farmer: farm spot is {Distance:F0}y away — travelling there", homeDistance);
                    pathfinder.Stop();
                    navigation.PathToPoint(farmer.StartingPoint, "farm spot", "mob_farmer_spot");
                    return null;
                }

                if (TryRecoverFromStuck(homeDistance, farmer.StartingPoint))
                {
                    return null;
                }

                if (pathfinder.GetState() == PathfindingState.Idle)
                {
                    IssuePath(farmer.StartingPoint);
                }

                MountWait.TryCastIfNeeded(
                    conditions,
                    objects,
                    farmer.StartingPoint,
                    movementConfig.ShouldAutoMount,
                    movementConfig.PreferredMountId,
                    zones.GetZone().IsInBasecamp(),
                    zones.GetZone());

                return null;
            }
        }

        readySinceUtc ??= DateTime.UtcNow;
        int free = MobFarmerPack.CountTowardMinimum(
            scanner.NotInCombat.Where(o => !GatheringHandler.SkippedMobs.IsSkipped(o.GameObjectId, DateTime.UtcNow)),
            config.CountSpecialMobsTowardMinimum);
        if (!MobFarmerRules.ShouldStartPull(
                free,
                config.MinimumMobsToStartLoop,
                DateTime.UtcNow - readySinceUtc.Value,
                TimeSpan.FromSeconds(config.StackingTimeoutSeconds)))
        {
            return null;
        }

        // Pulling right after a fight (or a Phantom Oracle Starfall) at low HP died to the first hit.
        if (objects.LocalPlayer is { MaxHp: > 0 } me && me.CurrentHp * 100 < me.MaxHp * (uint)config.MinimumHpPercentToPull)
        {
            if (!loggedLowHp)
            {
                loggedLowHp = true;
                logger.Info("Mob Farmer: waiting for HP ({Hp}% of {Min}%) before the next pull", me.CurrentHp * 100 / me.MaxHp, config.MinimumHpPercentToPull);
            }

            return null;
        }

        loggedLowHp = false;
        return FarmerPhase.Buffing;
    }

    private bool TryRecoverFromStuck(float distance, Vector3 goal)
    {
        FarmerWalkStuckAssist.Recovery recovery = stuckAssist.Tick(
            HomeWatchKey,
            distance,
            goal,
            pathfinder.GetState());

        switch (recovery)
        {
            case FarmerWalkStuckAssist.Recovery.Nudge:
                logger.Debug("Mob Farmer: stuck approaching farm spot — nudging sideways");
                pathfinder.Stop();
                IssuePath(FarmerWalkStuckAssist.LateralNudge(player.Position, goal));
                return true;

            case FarmerWalkStuckAssist.Recovery.RepathGoal:
                logger.Debug("Mob Farmer: still stuck on farm spot — repathing");
                pathfinder.Stop();
                IssuePath(goal);
                return true;

            case FarmerWalkStuckAssist.Recovery.GiveUp:
                logger.Info(
                    "Mob Farmer: could not reach farm spot after stuck recoveries — continuing from here");
                pathfinder.Stop();
                farmer.MarkArrivedAtSpot();
                stuckAssist.Reset();
                return true;

            default:
                return false;
        }
    }

    private void IssuePath(Vector3 destination)
    {
        pathfinder.PathfindAndMoveTo(new(destination)
        {
            AllowFlying = false,
            DistanceThreshold = PathArriveRange,
        });
    }
}
