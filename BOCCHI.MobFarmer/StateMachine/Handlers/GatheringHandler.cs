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

    private static readonly TimeSpan AiPullPatience = TimeSpan.FromSeconds(8);

    internal static readonly SkipList SkippedMobs = new(TimeSpan.FromMinutes(5));

    private ulong aiPullMobId;

    private readonly HashSet<ulong> pulledIds = [];

    private ulong walkMobId;

    private DateTime aiPullSinceUtc;

    public override void Exit(FarmerPhase next)
    {
        stuckAssist.Reset();
        aiPullMobId = 0;
        walkMobId = 0;
        pulledIds.Clear();
        base.Exit(next);
    }

    public override FarmerPhase? Handle()
    {
        List<IBattleNpc> inCombat = scanner.InCombat.ToList();
        List<IBattleNpc> notInCombat = scanner.NotInCombat
            .Where(o => !SkippedMobs.IsSkipped(o.GameObjectId, DateTime.UtcNow))
            .ToList();

        ReportLostPulls(inCombat);

        int gathered = MobFarmerPack.CountTowardMinimum(inCombat, config.CountSpecialMobsTowardMinimum);
        int wanted = farmer.EffectiveMinimumMobsToStartFight;
        if (gathered >= wanted)
        {
            logger.Info("Mob Farmer: gathered {Gathered} of {Wanted} — fighting", gathered, wanted);
            pathfinder.Stop();
            stuckAssist.Reset();
            return FarmerPhase.Stacking;
        }

        if (config.StackingTimeoutSeconds > 0
            && TimeInState >= TimeSpan.FromSeconds(config.StackingTimeoutSeconds)
            && inCombat.Count > 0)
        {
            logger.Info(
                "Mob Farmer: gather timeout ({Seconds}s) with {Gathered} of {Wanted} — fighting",
                (int)config.StackingTimeoutSeconds,
                gathered,
                wanted);
            pathfinder.Stop();
            stuckAssist.Reset();
            return FarmerPhase.Stacking;
        }

        if (notInCombat.Count == 0)
        {
            logger.Info("Mob Farmer: no more free enemies nearby, {Gathered} of {Wanted} gathered", gathered, wanted);
            pathfinder.Stop();
            stuckAssist.Reset();
            return inCombat.Count > 0 ? FarmerPhase.Stacking : FarmerPhase.Waiting;
        }

        // Nearest to you dragged the pack across the circle (early pulls gave up); nearest to the spot ran past close mobs.
        Vector3 spot = farmer.StartingPoint;
        List<IBattleNpc> ordered = notInCombat
            .OrderBy(o => spot.Distance2D(o.Position) + player.Position.Distance2D(o.Position))
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
        bool pulled = current.IsTargetingPlayer(objects.LocalPlayer);
        if (dist <= FarmerPullAssist.PullRange)
        {
            // Non-tanks have no pull tool; without the AI nothing ever starts the fight.
            if (player.IsTank())
            {
                pull.TryPull(current);
            }
            else if (!pulled)
            {
                // The rotation only attacks what is targeted; without this an "Auto-target off" setup never pulls.
                if (targets.Target?.GameObjectId != current.GameObjectId)
                {
                    targets.Target = current;
                }

                if (aiPullMobId != current.GameObjectId)
                {
                    aiPullMobId = current.GameObjectId;
                    aiPullSinceUtc = DateTime.UtcNow;
                }

                farmer.RequestAiPull();
                // vnav movement cancels the AI's cast, so stand still until the mob is tagged.
                if (farmer.CanAiPull
                    && dist <= player.GetAttackRange() + current.HitboxRadius
                    && DateTime.UtcNow - aiPullSinceUtc < AiPullPatience)
                {
                    pathfinder.Stop();
                    return null;
                }
            }
        }

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

        // The mob we were walking to is tagged (or no longer the closest free one): stop instead of walking onto its old spot.
        if (walkMobId != 0 && walkMobId != current.GameObjectId && pathfinder.GetState() != PathfindingState.Idle)
        {
            pathfinder.Stop();
            walkMobId = 0;
        }

        if (pathfinder.GetState() != PathfindingState.Idle)
        {
            return null;
        }

        if (!pulled && !EzThrottler.Throttle("MobFarmer::Gathering::Repath"))
        {
            return null;
        }

        if (IssuePath(destination))
        {
            walkMobId = current.GameObjectId;
        }

        return null;
    }

    private void ReportLostPulls(List<IBattleNpc> inCombat)
    {
        HashSet<ulong> now = inCombat.Select(m => m.GameObjectId).ToHashSet();
        foreach (ulong id in pulledIds)
        {
            if (!now.Contains(id) && objects.SearchById(id) is IBattleNpc { IsDead: false } lost)
            {
                logger.Info(
                    "Mob Farmer: lost {Name} while gathering ({Spot:F0}y from the spot, {You:F0}y from you), {Count} still on you",
                    lost.Name.TextValue,
                    farmer.StartingPoint.Distance2D(lost.Position),
                    player.Position.Distance2D(lost.Position),
                    now.Count);
            }
        }

        pulledIds.Clear();
        pulledIds.UnionWith(now);
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
                SkippedMobs.Skip(current.GameObjectId, DateTime.UtcNow);
                return true;

            default:
                return false;
        }
    }

    private bool IssuePath(Vector3 destination)
    {
        return pathfinder.PathfindAndMoveTo(new(destination)
        {
            AllowFlying = false,
            DistanceThreshold = PathArriveRange,
            ShouldSnapToFloor = true,
        });
    }

    private static Vector3 Destination(Vector3 mobPos, Vector3? next, float distToMob, bool mobPulled)
    {
        if (distToMob <= FarmerPullAssist.PullRange)
        {
            return mobPulled ? next ?? mobPos : mobPos;
        }

        return mobPos;
    }
}
