using BOCCHI.Common.Config;
using BOCCHI.Common.Data.Zones;
using BOCCHI.Common.Extensions;
using BOCCHI.MobFarmer.Data;
using BOCCHI.MobFarmer.Services;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using ECommons.Throttlers;
using Ocelot.Extensions;
using Ocelot.Services.Logger;
using Ocelot.Services.Pathfinding;
using Ocelot.Services.PlayerState;
using Ocelot.States.Flow;
using System.Numerics;

namespace BOCCHI.MobFarmer.StateMachine.Handlers;

public class GatheringHandler
(
    MobFarmerConfig config,
    IMobFarmer farmer,
    IMobScanner scanner,
    FarmerPullAssist pull,
    IObjectTable objects,
    ITargetManager targets,
    IPathfinder pathfinder,
    ICondition conditions,
    IPlayer player,
    ILogger<GatheringHandler> logger
) : FlowStateHandler<FarmerPhase>(FarmerPhase.Gathering)
{
    private const float PathArriveRange = 2f;

    private readonly FarmerWalkStuckAssist stuckAssist = new();

    private readonly HashSet<ulong> skippedMobIds = [];

    public override void Exit(FarmerPhase next)
    {
        stuckAssist.Reset();
        base.Exit(next);
    }

    public override FarmerPhase? Handle()
    {
        List<IBattleNpc> inCombat = scanner.InCombat.ToList();
        List<IBattleNpc> notInCombat = scanner.NotInCombat
            .Where(o => !skippedMobIds.Contains(o.GameObjectId))
            .ToList();

        if (MobFarmerPack.CountTowardMinimum(inCombat, config.CountSpecialMobsTowardMinimum)
            >= farmer.EffectiveMinimumMobsToStartFight)
        {
            pathfinder.Stop();
            stuckAssist.Reset();
            return FarmerPhase.Stacking;
        }

        // Gather timeout: stop chasing more packs and fight what we have (UI "Gather timeout").
        if (config.StackingTimeoutSeconds > 0
            && TimeInState >= TimeSpan.FromSeconds(config.StackingTimeoutSeconds)
            && inCombat.Count > 0)
        {
            pathfinder.Stop();
            stuckAssist.Reset();
            return FarmerPhase.Stacking;
        }

        if (notInCombat.Count == 0)
        {
            pathfinder.Stop();
            stuckAssist.Reset();
            return inCombat.Count > 0 ? FarmerPhase.Stacking : FarmerPhase.Waiting;
        }

        List<IBattleNpc> ordered = notInCombat
            .OrderBy(o => player.Position.Distance2D(o.Position))
            .ToList();
        IBattleNpc current = ordered[0];
        Vector3? nextPos = ordered.Count > 1
            ? ordered[1].Position
            : farmer.StackPoint;

        if (DismountAssist.TryDismount(conditions))
        {
            return null;
        }

        if (config.ShouldHandleTargeting
            && targets.Target?.GameObjectId != current.GameObjectId)
        {
            targets.Target = current;
        }

        float dist = player.Position.Distance2D(current.Position);
        if (dist <= FarmerPullAssist.PullRange)
        {
            pull.TryPull(current);
        }

        bool pulled = current.IsTargetingPlayer(objects.LocalPlayer);
        Vector3 destination = Destination(current.Position, nextPos, dist, pulled);

        FarmerWalkStuckAssist.Recovery recovery = stuckAssist.Tick(
            current.GameObjectId,
            dist,
            current.Position,
            pathfinder.GetState());

        if (recovery != FarmerWalkStuckAssist.Recovery.None
            && TryRecoverFromStuck(current, recovery))
        {
            return null;
        }

        if (pathfinder.GetState() != PathfindingState.Idle)
        {
            return null;
        }

        if (!pulled && !EzThrottler.Throttle("MobFarmer::Gathering::Repath"))
        {
            return null;
        }

        IssuePath(destination);
        return null;
    }

    private bool TryRecoverFromStuck(IBattleNpc current, FarmerWalkStuckAssist.Recovery recovery)
    {
        switch (recovery)
        {
            case FarmerWalkStuckAssist.Recovery.Nudge:
                logger.Debug(
                    "Mob Farmer: stuck approaching {Name} — nudging sideways",
                    current.Name.TextValue);
                pathfinder.Stop();
                IssuePath(FarmerWalkStuckAssist.LateralNudge(player.Position, current.Position));
                return true;

            case FarmerWalkStuckAssist.Recovery.RepathGoal:
                logger.Debug(
                    "Mob Farmer: still stuck on {Name} — repathing to mob",
                    current.Name.TextValue);
                pathfinder.Stop();
                IssuePath(current.Position);
                return true;

            case FarmerWalkStuckAssist.Recovery.GiveUp:
                logger.Info(
                    "Mob Farmer: giving up on {Name} after stuck recoveries — trying another mob",
                    current.Name.TextValue);
                pathfinder.Stop();
                skippedMobIds.Add(current.GameObjectId);
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
            ShouldSnapToFloor = true,
        });
    }

    /// <summary>
    ///     Walk toward the mob along player→mob, not current→next (which can aim through walls).
    ///     When already in pull range, hold on the current mob or step toward the next pack member.
    /// </summary>
    private static Vector3 Destination(Vector3 mobPos, Vector3? next, float distToMob, bool mobPulled)
    {
        if (distToMob <= FarmerPullAssist.PullRange)
        {
            return mobPulled ? next ?? mobPos : mobPos;
        }

        return mobPos;
    }
}
