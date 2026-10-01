using BOCCHI.Automator.Data;
using BOCCHI.Automator.Services;
using BOCCHI.Common.Config;
using BOCCHI.Common.Data.Fates;
using BOCCHI.Common.Data.Goals;
using BOCCHI.Common.Data.StateMemory;
using BOCCHI.Common.Data.Zones;
using BOCCHI.Common.Services;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using Ocelot.Extensions;
using Ocelot.Pathfinding.Extensions;
using Ocelot.Services.Logger;
using Ocelot.Services.Pathfinding;
using Ocelot.Services.PlayerState;
using Ocelot.States.Score;

namespace BOCCHI.Automator.StateMachine.Handlers;

public class InFateHandler
(
    IAutomatorMemory memory,
    IFateContext context,
    IObjectTable objects,
    ICondition conditions,
    IPathfinder pathfinder,
    AutoRotationController autoRotation,
    IPlayer playerState,
    AutomatorConfig config,
    IFateRepository fates,
    ILogger<InFateHandler> logger
) : ScoreStateHandler<AutomatorState, StatePriority>(AutomatorState.InFate)
{
    public override StatePriority GetScore()
    {
        if (!memory.TryRemember<GoalMemory>(out GoalMemory goal) || goal.Goal.GoalType is not FateGoal fateGoal)
        {
            return StatePriority.Never;
        }

        if (memory.TryRemember<CommittedFateMemory>(out CommittedFateMemory committed)
            && committed.IsFor(fateGoal.id)
            && fates.HasFate(fateGoal.id)
            && IsStillInFateFight(fateGoal.id))
        {
            return StatePriority.VeryHigh;
        }

        if (context.GetFateId() != fateGoal.id)
        {
            return StatePriority.Never;
        }

        if (memory.TryRemember<SuspendTravelForActivityMemory>(out SuspendTravelForActivityMemory _))
        {
            return StatePriority.VeryHigh;
        }

        if (config.CombatAutorotation.UsesCombatAutomation()
            && !context.IsInCombatWith(fateGoal.id)
            && objects.LocalPlayer is { } player
            && !IsWithinAiHandoff(player, fateGoal.id))
        {
            return StatePriority.Never;
        }

        return StatePriority.VeryHigh;
    }

    public override void Enter()
    {
        base.Enter();
        memory.TryAdd(new SuspendTravelForActivityMemory());
        memory.Forget<GoalPathStepMemory>();
        pathfinder.Stop();
        autoRotation.EnableForFate();
        memory.Forget<CommittedFateMemory>();
        FateId? entered = context.GetFateId();
        if (entered == null
            && memory.TryRemember<GoalMemory>(out GoalMemory goal)
            && goal.Goal.GoalType is FateGoal fateGoal)
        {
            entered = fateGoal.id;
        }

        if (entered is { } fateId)
        {
            memory.TryAdd(new CommittedFateMemory(fateId));
        }

        logger.Info("Entered FATE {Id} — travel suspended", entered?.Value.ToString() ?? "?");
    }

    public override void Exit(AutomatorState next)
    {
        // Drop SuspendTravel first so DisableAi actually turns combat off while down.
        // After raise, Pathfinding walks back to handoff if EventId alone is not enough.
        if (next == AutomatorState.Dead)
        {
            memory.Forget<SuspendTravelForActivityMemory>();
            autoRotation.DisableAi();
            logger.Info("Died in FATE — keeping commitment for raise");
            base.Exit(next);
            return;
        }

        memory.Forget<SuspendTravelForActivityMemory>();
        memory.Forget<CommittedFateMemory>();
        autoRotation.DisableAi();
        logger.Info("Left FATE — travel resumed");
        base.Exit(next);
    }

    public override void Handle()
    {
        if (objects.LocalPlayer is not { } player)
        {
            return;
        }

        if (config.CombatAutorotation.UsesCombatAutomation())
        {
            DismountAssist.TryDismount(conditions);
            if (!pathfinder.IsIdle())
            {
                pathfinder.Stop();
            }

            return;
        }

        FateId? liveId = context.GetFateId()
            ?? (memory.TryRemember<GoalMemory>(out GoalMemory handleGoal)
                && handleGoal.Goal.GoalType is FateGoal handleFate
                    ? handleFate.id
                    : null);
        List<IBattleNpc> fateTargets = liveId is { } id
            ? context.GetTargetsFor(id).ToList()
            : [];
        InitialCombatApproachMemory<FateId> approach = GetApproachMemory(liveId);
        if (CombatActivityHandler.HandleTargets(
                player,
                playerState,
                fateTargets,
                conditions,
                pathfinder,
                "InFate",
                approach.IsPending,
                stopPathfinderInCombat: true))
        {
            approach.Complete();
        }
    }

    private InitialCombatApproachMemory<FateId> GetApproachMemory(FateId? fateId)
    {
        if (!memory.TryRemember(out InitialCombatApproachMemory<FateId> approach))
        {
            approach = new();
            memory.TryAdd(approach);
        }

        approach.Track(fateId);
        return approach;
    }

    private bool IsStillInFateFight(FateId id)
    {
        if (context.GetFateId() == id || context.IsInCombatWith(id))
        {
            return true;
        }

        if (objects.LocalPlayer is not { } player)
        {
            return false;
        }

        float nearest = float.MaxValue;
        foreach (IBattleNpc target in context.GetTargetsFor(id))
        {
            nearest = MathF.Min(nearest, player.Position.Distance2D(target.Position) - target.HitboxRadius);
        }

        Fate? live = fates.Snapshot().FirstOrDefault(f => f.Id.Value == id.Value);
        float toCenter = live != null ? player.Position.Distance2D(live.Position) : float.MaxValue;
        float radius = live?.Radius ?? 0f;
        return NavigationConstants.IsWithinFateCommitment(toCenter, radius, nearest);
    }

    private bool IsWithinAiHandoff(IGameObject player, FateId id)
    {
        float nearest = float.MaxValue;
        foreach (IBattleNpc target in context.GetTargets())
        {
            nearest = MathF.Min(nearest, player.Position.Distance2D(target.Position) - target.HitboxRadius);
        }

        Fate? live = fates.Snapshot().FirstOrDefault(f => f.Id.Value == id.Value);
        float toCenter = live != null ? player.Position.Distance2D(live.Position) : float.MaxValue;
        return NavigationConstants.IsWithinFateAiHandoff(toCenter, nearest);
    }
}
