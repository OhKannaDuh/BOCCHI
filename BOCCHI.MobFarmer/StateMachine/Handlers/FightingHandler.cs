using BOCCHI.Common.Config;
using BOCCHI.Common.Data.Zones;
using BOCCHI.Common.Targeting;
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

public class FightingHandler
(
    MobFarmerConfig config,
    AutomatorConfig automatorConfig,
    MovementConfig movementConfig,
    IMobFarmer farmer,
    IFarmerCombatController combat,
    IMobScanner scanner,
    ITargetManager targets,
    ICondition conditions,
    IObjectTable objects,
    IPathfinder pathfinder,
    IZoneProvider zones,
    IPlayer player,
    ILogger<FightingHandler> logger
) : FlowStateHandler<FarmerPhase>(FarmerPhase.Fighting)
{
    private const float HomeArriveRange = 2f;

    private const ulong HomeWatchKey = 0;

    private readonly FarmerWalkStuckAssist stuckAssist = new();

    private bool abandonHomeReturn;

    public override void Enter()
    {
        base.Enter();
        // Stacking may still be walking — BossMod AI cannot dodge while vnav owns movement.
        pathfinder.Stop();
        combat.EnableFighting();
    }

    public override void Exit(FarmerPhase next)
    {
        combat.Disable();
        stuckAssist.Reset();
        abandonHomeReturn = false;
        base.Exit(next);
    }

    public override FarmerPhase? Handle()
    {
        List<IBattleNpc> inCombat = scanner.InCombat.ToList();
        bool anyInCombat = inCombat.Count > 0;
        bool useAi = automatorConfig.CombatAutorotation.UsesCombatAutomation();

        if (anyInCombat || conditions[ConditionFlag.InCombat])
        {
            combat.EnableFighting();
            if (DismountAssist.TryDismount(conditions))
            {
                return null;
            }

            if (!useAi
                && config.ShouldHandleTargeting
                && inCombat.Count > 0
                && EzThrottler.Throttle("MobFarmer::Fighting::Target", 250))
            {
                IBattleNpc? target = TargetHelper.Select(inCombat, player.Position, config.ForceTargetCentralEnemy);
                if (target != null)
                {
                    targets.Target = target;
                }
            }

            if (useAi)
            {
                pathfinder.Stop();
            }

            return null;
        }

        combat.Disable();

        bool shouldReturnHome = config.ReturnToStartInWaitingPhase
                                && player.Position.Distance2D(farmer.StartingPoint) >= config.MinEuclideanDistanceToReturnHome;

        if (shouldReturnHome)
        {
            float homeDistance = player.Position.Distance2D(farmer.StartingPoint);

            if (abandonHomeReturn)
            {
                abandonHomeReturn = false;
                return FarmerPhase.Waiting;
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
                zones.GetZone().IsInBasecamp());

            return homeDistance <= HomeArriveRange ? FarmerPhase.Waiting : null;
        }

        pathfinder.Stop();
        stuckAssist.Reset();
        return FarmerPhase.Waiting;
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
                logger.Debug("Mob Farmer: stuck returning home — nudging sideways");
                pathfinder.Stop();
                IssuePath(FarmerWalkStuckAssist.LateralNudge(player.Position, goal));
                return true;

            case FarmerWalkStuckAssist.Recovery.RepathGoal:
                logger.Debug("Mob Farmer: still stuck returning home — repathing");
                pathfinder.Stop();
                IssuePath(goal);
                return true;

            case FarmerWalkStuckAssist.Recovery.GiveUp:
                logger.Info(
                    "Mob Farmer: could not return home after stuck recoveries — resuming from here");
                pathfinder.Stop();
                stuckAssist.Reset();
                abandonHomeReturn = true;
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
            DistanceThreshold = HomeArriveRange,
        });
    }
}
