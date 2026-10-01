using System.Numerics;
using BOCCHI.Common.Targeting;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using ECommons.Throttlers;
using Ocelot.Actions;
using Ocelot.Extensions;
using Ocelot.Pathfinding.Extensions;
using Ocelot.Services.Pathfinding;
using Ocelot.Services.PlayerState;

namespace BOCCHI.Automator.StateMachine.Handlers;

internal static class CombatActivityHandler
{
    private const float HitboxEdgeTolerance = 1f;

    private const float RangedStandoffRange = 15f;

    private const float DismountRange = 20f;

    public static bool HandleTargets(
        IGameObject player,
        IPlayer playerState,
        IEnumerable<IBattleNpc> targets,
        ICondition conditions,
        IPathfinder pathfinder,
        string throttlePrefix,
        bool shouldApproachTarget,
        bool stopPathfinderInCombat = false
    )
    {
        List<IBattleNpc> list = targets as List<IBattleNpc> ?? targets.ToList();
        IBattleNpc? target = TargetHelper.Select(list, player.Position, preferCentroid: false);
        if (target == null)
        {
            return false;
        }

        bool isMelee = playerState.IsMelee();
        float distance = player.Position.Distance2D(target.Position) - target.HitboxRadius;
        bool nearTarget = distance <= DismountRange;

        if (conditions[ConditionFlag.Mounted]
            && nearTarget
            && EzThrottler.Throttle($"{throttlePrefix}::Unmount")
            && Actions.Unmount.CanCast())
        {
            Actions.Unmount.Cast();
            pathfinder.Stop();
            return false;
        }

        if (stopPathfinderInCombat && conditions[ConditionFlag.InCombat])
        {
            pathfinder.Stop();
            return true;
        }

        if (IsInEngagementRange(distance, isMelee))
        {
            pathfinder.Stop();
            return true;
        }

        if (!shouldApproachTarget
            || !EzThrottler.Throttle($"{throttlePrefix}::Approach", 500)
            || !pathfinder.IsIdle())
        {
            return false;
        }

        PathToEngagement(player, target, isMelee, pathfinder);
        return false;
    }

    private static bool IsInEngagementRange(float distancePastHitbox, bool isMelee)
    {
        return isMelee
            ? distancePastHitbox <= HitboxEdgeTolerance
            : distancePastHitbox <= RangedStandoffRange + 0.5f;
    }

    private static void PathToEngagement(
        IGameObject player,
        IBattleNpc target,
        bool isMelee,
        IPathfinder pathfinder
    )
    {
        if (isMelee)
        {
            pathfinder.PathfindAndMoveTo(new PathfinderConfig(target.Position)
            {
                DistanceThreshold = Math.Max(0.5f, target.HitboxRadius + HitboxEdgeTolerance),
                ShouldSnapToFloor = true,
            });
            return;
        }

        Vector3 standOff = target.Position.GetApproachPosition(
            player.Position,
            target.HitboxRadius + RangedStandoffRange);
        pathfinder.PathfindAndMoveTo(new PathfinderConfig(standOff)
        {
            DistanceThreshold = 1f,
            ShouldSnapToFloor = true,
        });
    }
}
