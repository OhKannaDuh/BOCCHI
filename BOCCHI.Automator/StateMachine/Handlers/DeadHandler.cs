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
    private const int RaiseOfferExpiryMarginSeconds = 5;

    private DateTime? raiseOfferSeenUtc;

    private string raiseOfferCaster = string.Empty;

    public override StatePriority GetScore() =>
        player.Conditions[ConditionFlag.Unconscious] ? StatePriority.Always : StatePriority.Never;

    public override void Enter()
    {
        base.Enter();
        ForgetRaiseOffer();
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
        if (!config.AutoAcceptRaise)
        {
            return;
        }

        if (!RaisePrompt.TryGetPending(out string caster, out int timeLeft))
        {
            ForgetRaiseOffer();
            return;
        }

        if (raiseOfferSeenUtc == null || raiseOfferCaster != caster)
        {
            raiseOfferSeenUtc = DateTime.UtcNow;
            raiseOfferCaster = caster;
            logger.Info(
                "Raise offered by {Caster} (timer {TimeLeft}), accepting in {Delay}s",
                caster,
                timeLeft,
                config.AutoAcceptRaiseDelaySeconds);
        }

        bool delayElapsed = DateTime.UtcNow - raiseOfferSeenUtc >= TimeSpan.FromSeconds(config.AutoAcceptRaiseDelaySeconds);
        // Never let the delay run the offer out. Assumes the agent timer counts seconds (the log
        // above prints it raw); if it is finer-grained this only fires at the very end — no harm.
        bool offerExpiring = timeLeft <= RaiseOfferExpiryMarginSeconds;
        if ((!delayElapsed && !offerExpiring) || !EzThrottler.Throttle("Dead::AcceptRaise", 1000))
        {
            return;
        }

        if (RaisePrompt.TryAccept(out caster))
        {
            logger.Info("Accepted raise from {Caster}", caster);
            ForgetRaiseOffer();
        }
    }

    private void ForgetRaiseOffer()
    {
        raiseOfferSeenUtc = null;
        raiseOfferCaster = string.Empty;
    }
}
