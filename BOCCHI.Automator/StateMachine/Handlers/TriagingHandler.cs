using BOCCHI.Automator.Data;
using BOCCHI.Automator.Services;
using BOCCHI.Common.Config;
using BOCCHI.Common.Data.OccultCrescent;
using BOCCHI.Common.Data.StateMemory;
using BOCCHI.Common.Data.SupportJobs;
using BOCCHI.Common.Data.Zones;
using BOCCHI.Common.Services;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using ECommons.Throttlers;
using FFXIVClientStructs.FFXIV.Client.Game;
using Ocelot.Extensions;
using Ocelot.Pathfinding.Extensions;
using Ocelot.Services.Logger;
using Ocelot.Services.Pathfinding;
using Ocelot.States.Score;
using Action = Ocelot.Actions.Action;
using ActionCastScope = Ocelot.Actions.ActionCastScope;

namespace BOCCHI.Automator.StateMachine.Handlers;

/// <summary>
///     After FATE/CE with nearby dead players: Chemist or White Mage raise → restore job → continue.
/// </summary>
public class TriagingHandler
(
    IAutomatorContext context,
    IAutomatorMemory memory,
    AutomatorConfig config,
    ICondition conditions,
    IObjectTable objects,
    ITargetManager targetManager,
    ISupportJobFactory supportJobs,
    ISupportJobChanger changer,
    IPathfinder pathfinder,
    ILogger<TriagingHandler> logger
) : ScoreStateHandler<AutomatorState, StatePriority>(AutomatorState.Triaging)
{
    private static readonly TimeSpan JobSwapSettle = TimeSpan.FromSeconds(5);

    private static readonly TimeSpan SessionTimeout = TimeSpan.FromSeconds(90);

    private DateTimeOffset sessionStartedUtc = DateTimeOffset.MinValue;

    private SupportJobId raiseJob = SupportJobId.PhantomChemist;

    private Action raiseAction = new(ActionType.Action, PhantomActions.Revive);

    public override StatePriority GetScore()
    {
        if ((!context.IsIllegalMode && !context.IsCompletionist) || !config.EnableTriageMode)
        {
            TriageSession.Clear(memory);
            return StatePriority.Never;
        }

        if (conditions[ConditionFlag.Unconscious])
        {
            return StatePriority.Never;
        }

        if (memory.TryRemember<TriagingMemory>(out TriagingMemory _))
        {
            // Stay Critical through combat so Returning's VeryHigh latch cannot steal the session
            // mid-raise (Vertigo: status "Returning to camp" while standing still after a FATE).
            return StatePriority.Critical;
        }

        if (!memory.TryRemember<PendingTriageMemory>(out PendingTriageMemory _))
        {
            return StatePriority.Never;
        }

        if (!TriageRaiseJob.AnyUnlocked(supportJobs) || !RaiseableCorpses.Any(objects))
        {
            memory.Forget<PendingTriageMemory>();
            return StatePriority.Never;
        }

        // Score Always while pending so Return (VeryHigh) cannot own the UI until triage starts.
        return StatePriority.Always;
    }

    public override void Enter()
    {
        base.Enter();
        sessionStartedUtc = DateTimeOffset.UtcNow;
        pathfinder.Stop();
        memory.TryAdd<TriagingMemory>();

        if (!TriageRaiseJob.TrySelect(supportJobs, config.PreferredTriageRaiseJob, out raiseJob))
        {
            raiseJob = SupportJobId.PhantomChemist;
        }

        raiseAction = new Action(ActionType.Action, TriageRaiseJob.RaiseActionId(raiseJob));
        logger.Info("Triage Mode: raising nearby players with {Job}", raiseJob);
    }

    public override void Exit(AutomatorState next)
    {
        base.Exit(next);
        FinishTriage();
    }

    public override void Handle()
    {
        if (!EzThrottler.Throttle("TriagingHandler::Gate", 250))
        {
            return;
        }

        if (DateTimeOffset.UtcNow - sessionStartedUtc >= SessionTimeout)
        {
            logger.Info("Triage Mode: session timeout");
            FinishTriage();
            return;
        }

        if (conditions[ConditionFlag.Unconscious])
        {
            return;
        }

        if (conditions[ConditionFlag.InCombat])
        {
            return;
        }

        if (DismountAssist.TryDismount(conditions))
        {
            return;
        }

        if (PhantomJobChangeGate.IsBlocked(conditions))
        {
            return;
        }

        IPlayerCharacter? corpse = RaiseableCorpses.FindNearest(objects);
        if (corpse == null)
        {
            logger.Info("Triage Mode: done — restoring job then Return");
            FinishTriage();
            return;
        }

        if (!supportJobs.TryGetCurrent(out SupportJob current) || current.Id != raiseJob)
        {
            TrySwapToRaiseJob(current);
            return;
        }

        if (conditions[ConditionFlag.Casting] || conditions[ConditionFlag.Casting87])
        {
            return;
        }

        float distance = objects.LocalPlayer is { } self
            ? self.Position.Distance2D(corpse.Position)
            : float.MaxValue;

        if (distance > RaiseableCorpses.CastRangeYalms)
        {
            if (pathfinder.IsIdle())
            {
                pathfinder.PathfindAndMoveTo(new(corpse.Position)
                {
                    DistanceThreshold = RaiseableCorpses.CastRangeYalms - 2f,
                    ShouldSnapToFloor = true
                });
            }

            return;
        }

        pathfinder.Stop();
        targetManager.Target = corpse;

        if (!raiseAction.CanCast())
        {
            return;
        }

        if (TryCastRaise(corpse))
        {
            logger.Debug("Triage Mode: raise on {Name}", corpse.Name.TextValue);
        }
    }

    private void TrySwapToRaiseJob(SupportJob? current)
    {
        if (DateTimeOffset.UtcNow - sessionStartedUtc < JobSwapSettle)
        {
            return;
        }

        if (changer.IsBusy() || !EzThrottler.Throttle("TriagingHandler::JobSwap", 2500))
        {
            return;
        }

        if (current is { } job
            && job.Id != raiseJob
            && !memory.TryRemember<TriageSupportJobMemory>(out TriageSupportJobMemory _))
        {
            memory.TryAdd(new TriageSupportJobMemory(job.Id));
        }

        changer.Change(raiseJob);
    }

    private void FinishTriage()
    {
        pathfinder.Stop();
        TriageSession.Clear(memory);
    }

    private unsafe bool TryCastRaise(IGameObject target)
    {
        using (ActionCastScope.SuppressPathfindCancel())
        {
            return ActionManager.Instance()->UseAction(ActionType.Action, raiseAction.Id, target.GameObjectId);
        }
    }
}
