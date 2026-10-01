using BOCCHI.Automator.Data;
using BOCCHI.Automator.Services;
using BOCCHI.Common.Data.StateMemory;
using BOCCHI.Common.Services;
using Dalamud.Game.ClientState.Conditions;
using Ocelot.Chain;
using Ocelot.Services.Pathfinding;
using Ocelot.Services.PlayerState;
using Ocelot.States.Score;

namespace BOCCHI.Automator.StateMachine.Handlers;

public class DeadHandler
(
    IPlayer player,
    IAutomatorMemory memory,
    IPathfinder pathfinder,
    IChainManager chains,
    AutoRotationController autoRotation
) : ScoreStateHandler<AutomatorState, StatePriority>(AutomatorState.Dead)
{
    public override StatePriority GetScore() =>
        player.Conditions[ConditionFlag.Unconscious] ? StatePriority.Always : StatePriority.Never;

    public override void Enter()
    {
        base.Enter();
        memory.Forget<ReturningStateMemory>();
        memory.Forget<GoalPathStepMemory>();
        chains.CancelAll();
        pathfinder.Stop();
    }

    public override void Exit(AutomatorState next)
    {
        autoRotation.OnRevived();
        base.Exit(next);
    }

    public override void Handle()
    {
    }
}
