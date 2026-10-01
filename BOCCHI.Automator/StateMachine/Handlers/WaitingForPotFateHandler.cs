using BOCCHI.Automator.Data;
using BOCCHI.Automator.Services;
using BOCCHI.Common.Config;
using BOCCHI.Common.Data.Fates;
using BOCCHI.Common.Data.Goals;
using BOCCHI.Common.Data.StateMemory;
using BOCCHI.Common.Data.Zones;
using BOCCHI.Common.Data.Zones.Graph;
using BOCCHI.Common.Services;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using ECommons.Throttlers;
using Ocelot.Actions;
using Ocelot.Chain;
using Ocelot.Extensions;
using Ocelot.Pathfinding.Extensions;
using Ocelot.Services.Pathfinding;
using Ocelot.States.Score;
using System.Numerics;

namespace BOCCHI.Automator.StateMachine.Handlers;

public class WaitingForPotFateHandler
(
    IAutomatorMemory memory,
    IObjectTable objects,
    ICondition conditions,
    IPathfinder pathfinder,
    IChainManager manager,
    IFateRepository fates,
    IZoneProvider zones,
    MovementConfig movement,
    AutoRotationController autoRotation
) : ScoreStateHandler<AutomatorState, StatePriority>(AutomatorState.WaitingForPotFate)
{
    public override StatePriority GetScore()
    {
        if (objects.LocalPlayer is not { } player)
        {
            return StatePriority.Never;
        }

        if (!memory.TryRemember<GoalMemory>(out GoalMemory goal) || goal.Goal.GoalType is not FateGoal fateGoal)
        {
            return StatePriority.Never;
        }

        if (!zones.GetZone().IsPotFate(fateGoal.id.Value))
        {
            return StatePriority.Never;
        }

        if (fates.HasFate(fateGoal.id))
        {
            return StatePriority.Never;
        }

        if (!TryGetPotCenter(fateGoal.id, out Vector3 potCenter))
        {
            return StatePriority.Never;
        }

        float dist = player.Position.Distance2D(potCenter);
        if (dist > NavigationConstants.PotPrepositionMaxRadius * 1.5f)
        {
            return StatePriority.Never;
        }

        if (dist > NavigationConstants.PotPrepositionMaxRadius)
        {
            return StatePriority.Normal;
        }

        return StatePriority.VeryHigh;
    }

    public override void Enter()
    {
        base.Enter();
        autoRotation.DisableAi();
        manager.CancelAll();
        memory.Forget<GoalPathStepMemory>();
        memory.TryAdd<WaitingForPotFateMemory>();
        pathfinder.Stop();
    }

    public override void Exit(AutomatorState next)
    {
        base.Exit(next);
        memory.Forget<WaitingForPotFateMemory>();
    }

    public override void Handle()
    {
        if (objects.LocalPlayer is not { } player)
        {
            return;
        }

        if (!memory.TryRemember<GoalMemory>(out GoalMemory goal) || goal.Goal.GoalType is not FateGoal fateGoal)
        {
            return;
        }

        if (fates.HasFate(fateGoal.id) || !TryGetPotCenter(fateGoal.id, out Vector3 potCenter))
        {
            return;
        }

        float dist = player.Position.Distance2D(potCenter);
        if (dist > NavigationConstants.PotPrepositionMaxRadius)
        {
            Vector3 approach = NavigationApproach.GetPotPrepositionPosition(potCenter, player.Position);

            if (pathfinder.IsIdle())
            {
                pathfinder.PathfindAndMoveTo(new(approach));
            }

            MountWait.TryCastIfNeeded(
                conditions,
                objects,
                approach,
                movement.ShouldAutoMount,
                movement.PreferredMountId,
                zones.GetZone().IsInBasecamp());

            return;
        }

        if (conditions[ConditionFlag.Mounted]
            && EzThrottler.Throttle("WaitingForPotFate::Unmount")
            && Actions.Unmount.CanCast())
        {
            Actions.Unmount.Cast();
            pathfinder.Stop();
        }
    }

    private bool TryGetPotCenter(FateId id, out Vector3 potCenter)
    {
        ActivityData? data = zones.GetZone().GetPotFateData().FirstOrDefault(p => p.Id == id.Value);
        if (data == null)
        {
            potCenter = default;
            return false;
        }

        potCenter = data.Position;
        return true;
    }
}
