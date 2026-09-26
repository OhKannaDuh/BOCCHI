using BOCCHI.Automator.Data;
using BOCCHI.Automator.Services;
using BOCCHI.Common.Config;
using BOCCHI.Common.Data.StateMemory;
using BOCCHI.Common.Services;
using Dalamud.Game.ClientState.Conditions;
using ECommons.Throttlers;
using Ocelot.Chain;
using Ocelot.Services.Logger;
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
    AutoRotationController autoRotation,
    AutomatorConfig config,
    ILogger<DeadHandler> logger
) : ScoreStateHandler<AutomatorState, StatePriority>(AutomatorState.Dead)
{
    public override StatePriority GetScore() =>
        player.Conditions[ConditionFlag.Unconscious] ? StatePriority.Always : StatePriority.Never;

    public override void Enter()
    {
        base.Enter();
        // Stop any in-flight Return so death prompts aren't auto-accepted.
        memory.Forget<ReturningStateMemory>();
        memory.Forget<GoalPathStepMemory>();
        // Cancel pot-chest / travel opens too — PathStep-only cancel left Interact spam while dead.
        chains.CancelAll();
        pathfinder.Stop();
    }

    public override void Exit(AutomatorState next)
    {
        // Force RSR/Wrath to re-issue Enable on In CE Enter — Henched IPC during unconscious
        // can no-op while we still cache "applied".
        autoRotation.OnRevived();
        base.Exit(next);
    }

    public override void Handle()
    {
        if (!config.AutoAcceptRaise || !EzThrottler.Throttle("Dead::AcceptRaise", 1000))
        {
            return;
        }

        if (RaisePrompt.TryAccept(out string caster))
        {
            logger.Info("Accepted raise from {Caster}", caster);
        }
    }
}
