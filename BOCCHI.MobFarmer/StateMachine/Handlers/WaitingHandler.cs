using BOCCHI.Common.Config;
using BOCCHI.Common.Data.Zones;
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
    ILogger<WaitingHandler> logger
) : FlowStateHandler<FarmerPhase>(FarmerPhase.Waiting)
{
    private const float ArriveRange = 8f;

    private const float PathArriveRange = 2f;

    private const ulong HomeWatchKey = 0;

    private readonly FarmerWalkStuckAssist stuckAssist = new();

    public override void Exit(FarmerPhase next)
    {
        stuckAssist.Reset();
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
            }
            else
            {
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
                    zones.GetZone().IsInBasecamp());

                return null;
            }
        }

        int free = MobFarmerPack.CountTowardMinimum(scanner.NotInCombat, config.CountSpecialMobsTowardMinimum);
        if (free == 0)
        {
            return null;
        }

        return free >= config.MinimumMobsToStartLoop ? FarmerPhase.Buffing : null;
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
