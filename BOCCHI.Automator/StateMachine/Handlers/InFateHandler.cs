using BOCCHI.Automator.Data;
using BOCCHI.Automator.Data.Goals;
using BOCCHI.Automator.Data.StateMemory;
using BOCCHI.Automator.Services;
using BOCCHI.Common.Data.Goals;
using BOCCHI.Common.Data.StateMemory;
using BOCCHI.Common.Services;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using ECommons.Throttlers;
using Ocelot.Actions;
using Ocelot.Extensions;
using Ocelot.States.Score;

namespace BOCCHI.Automator.StateMachine.Handlers;

public class InFateHandler(
    IAutomatorMemory memory,
    IFateContext context,
    IObjectTable objects,
    ICondition conditions,
    ITargetManager targetManager
) : ScoreStateHandler<AutomatorState, StatePriority>(AutomatorState.InFate)
{
    public override StatePriority GetScore()
    {
        if (!memory.TryRemember<GoalMemory>(out var goal) || goal.Goal.GoalType is not FateGoal fateGoal)
        {
            return StatePriority.Never;
        }

        return context.GetFateId() == fateGoal.id ? StatePriority.High : StatePriority.Never;
    }

    public override void Handle()
    {
        if (objects.LocalPlayer is not { } player)
        {
            return;
        }

        var targets = context.GetFateTargets().ToList();
        if (targets.Count == 0)
        {
            return;
        }

        var target = targets.First();
        if (EzThrottler.Throttle("InFate::Target") && targetManager.Target == null)
        {
            targetManager.Target = target;
        }

        var distance = player.Position.Distance2D(target.Position) - target.HitboxRadius;
        if (distance <= 5f && conditions[ConditionFlag.Mounted])
        {
            if (EzThrottler.Throttle("InFate::Unmount") && Actions.Unmount.CanCast())
            {
                Actions.Unmount.Cast();
            }
        }
    }
}
