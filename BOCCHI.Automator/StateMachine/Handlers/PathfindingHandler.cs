using BOCCHI.Automator.Data;
using BOCCHI.Automator.Services;
using BOCCHI.Common;
using BOCCHI.Common.Config;
using BOCCHI.Common.Data.Fates;
using BOCCHI.Common.Data.Goals;
using BOCCHI.Common.Data.Paths;
using BOCCHI.Common.Data.StateMemory;
using BOCCHI.Common.Data.Zones;
using BOCCHI.Common.Services;
using BOCCHI.Common.Services.Paths;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using ECommons.Throttlers;
using Ocelot.Chain;
using Ocelot.Extensions;
using Ocelot.Services.Logger;
using Ocelot.Services.Pathfinding;
using Ocelot.Services.Translation;
using Ocelot.States.Score;
using Ocelot.Windows;
using System.Numerics;

namespace BOCCHI.Automator.StateMachine.Handlers;

public class PathfindingHandler
(
    IAutomatorMemory memory,
    IPathStepExecutor pathStepExecutor,
    IChainManager manager,
    IObjectTable objects,
    IPathfinder pathfinder,
    ITargetManager targetManager,
    IZoneProvider zones,
    AutomatorConfig config,
    MovementConfig movement,
    UIConfig uiConfig,
    ICondition conditions,
    IChatGui chat,
    ITranslator<MainWindow> translator,
    AutoRotationController autoRotation,
    ILogger<PathfindingHandler> logger
) : ScoreStateHandler<AutomatorState, StatePriority>(AutomatorState.Pathfinding)
{
    private static readonly TimeSpan MountBeforePauseTimeout = TimeSpan.FromSeconds(8);

    private Task<ChainResult>? currentPathTask;

    private string? pendingPauseReason;

    private DateTime mountBeforePauseDeadline = DateTime.MinValue;

    public override void Enter()
    {
        base.Enter();
        // Drop leftover combat target so rotations don't pull trash mid-path.
        targetManager.Target = null;
        autoRotation.DisableAi();
    }

    public override void Exit(AutomatorState next)
    {
        base.Exit(next);

        // Don't cancel full pathing on a same-frame return handoff (restart loop).
        // Cancel leftover PathStep chains + stop the pathfinder; ReturningHandler.Enter
        // also stops vnav so a lingering move-to cannot cancel the Return cast.
        if (next == AutomatorState.Returning)
        {
            currentPathTask = null;
            PathStepSoftStop.Cancel(manager);
            pathfinder.Stop();
            return;
        }

        // Soft-interrupt (e.g. Completionist survey click) owns vnav via ActivityGoto — leave it alone.
        if (memory.TryRemember<NavigationInterruptedMemory>(out NavigationInterruptedMemory _))
        {
            currentPathTask = null;
            pendingPauseReason = null;
            PathStepSoftStop.Cancel(manager);
            return;
        }

        ResetPathfinding();
    }

    public override StatePriority GetScore()
    {
        if (memory.TryRemember<ApplyingBuffsMemory>(out ApplyingBuffsMemory _))
        {
            return StatePriority.Never;
        }

        if (memory.TryRemember<SuspendTravelForActivityMemory>(out SuspendTravelForActivityMemory _))
        {
            return StatePriority.Never;
        }

        // Hold still for pot chest farm — leftover GoalPathStep must not Return/TP away first.
        if (memory.TryRemember<PotChestFarmMemory>(out PotChestFarmMemory _)
            || memory.TryRemember<PendingPotChestFarmMemory>(out PendingPotChestFarmMemory _))
        {
            return StatePriority.Never;
        }

        return memory.TryRemember<GoalPathStepMemory>(out GoalPathStepMemory _) ? StatePriority.High : StatePriority.Never;
    }

    public override void Handle()
    {
        if (objects.LocalPlayer is not { } player)
        {
            return;
        }

        if (pendingPauseReason != null && FinishMountBeforePause())
        {
            return;
        }

        if (!memory.TryRemember<GoalPathStepMemory>(out GoalPathStepMemory path))
        {
            ResetPathfinding();
            return;
        }

        path.Update();

        // Route calc found nothing while still far from the goal — don't hang forever.
        if (path.RoutingFailed && currentPathTask == null)
        {
            string message = translator.T(".automation.automator.path_routing_failed");
            BocchiChat.PrintError(chat, uiConfig, message);
            PauseForManualPathing(message);
            return;
        }

        // Teleport-only mode: calc produced no Return/Teleport steps → pause for manual.
        if (path.PauseWhenPlanCompletes && path.IsEmptyPlan && currentPathTask == null)
        {
            BeginMountThenPause(TeleportOnlyMessage("no travel steps left"));
            return;
        }

        if (currentPathTask != null)
        {
            // Remount mid-route if Treasure Sight (or anything else) left us on foot.
            if (path.GetNextPathStep()?.PathStepData is Pathfind(var destination, _))
            {
                IZone zone = zones.GetZone();
                MountWait.TryCastIfNeeded(
                    conditions,
                    objects,
                    destination,
                    ShouldAutoMountToward(destination, zone),
                    movement.PreferredMountId,
                    zone.IsInBasecamp(),
                    zone);
            }

            if (currentPathTask.IsCompleted)
            {
                if (currentPathTask.Status == TaskStatus.RanToCompletion)
                {
                    ChainResult result = currentPathTask.Result;
                    if (result.IsSuccess)
                    {
                        logger.Debug("Finished current task step...");
                        PathStepKind completedKind = path.GetNextPathStep()?.Kind ?? PathStepKind.Pathfind;
                        path.DequeuePathStep();

                        if (path.PauseWhenPlanCompletes
                            && path.GetNextPathStep() == null
                            && completedKind is PathStepKind.Teleport or PathStepKind.Return)
                        {
                            currentPathTask = null;
                            memory.Forget<BaseTeleportDelayMemory>();
                            BeginMountThenPause(TeleportOnlyMessage("arrived at aetheryte"));
                            return;
                        }
                    }
                    else if (result.IsCanceled)
                    {
                        // Soft-stop / CE wait handoff / combat cancel — keep the goal and replan.
                        ReplanAfterPathCancel("Path step canceled");
                        return;
                    }
                    else
                    {
                        logger.Warning("Path step failed: {Error}", result.ErrorMessage ?? "unknown");
                        pathfinder.Stop();

                        // Keep Teleport — dequeuing skips the hop and leaves you stuck outside
                        // the pad (or walking the long way). Approach retries next tick.
                        if (path.GetNextPathStep()?.Kind != PathStepKind.Teleport)
                        {
                            path.DequeuePathStep();
                        }
                    }
                }
                else if (currentPathTask.IsCanceled)
                {
                    ReplanAfterPathCancel("Path step task canceled");
                    return;
                }
                else
                {
                    logger.Warning("Path step task faulted");
                    pathfinder.Stop();
                }

                currentPathTask = null;
            }

            return;
        }

        if (currentPathTask == null && path.GetNextPathStep() is { } step)
        {
            if (step.PathStepData is Return)
            {
                logger.Debug("Handing off return step to ReturningHandler...");
                memory.TryAdd(new ReturningStateMemory(ReturnDelay.Roll(config)));
                path.DequeuePathStep();

                if (path.PauseWhenPlanCompletes && path.GetNextPathStep() == null)
                {
                    BeginMountThenPause(TeleportOnlyMessage("returned to camp"));
                }

                return;
            }

            if (step.PathStepData is Teleport)
            {
                // Return was handed off — wait until it finishes. Otherwise Teleport walks to
                // the nearest shard and Lifestream opens short of camp.
                if (memory.TryRemember<ReturningStateMemory>(out ReturningStateMemory _))
                {
                    return;
                }

                if (zones.GetZone().IsInBasecamp() && !WaitForBaseTeleportDelay())
                {
                    return;
                }
            }

            logger.Debug("Starting next task step...");
            memory.Forget<BaseTeleportDelayMemory>();
            currentPathTask = pathStepExecutor.Execute(step);
            return;
        }

        // Empty plan (already at destination) — keep GoalPathStepMemory so Automator doesn't recreate.
        if (!path.IsValid)
        {
            memory.Forget<GoalPathStepMemory>();
        }
    }

    /// <returns>False while still waiting; true when ready to teleport.</returns>
    private bool WaitForBaseTeleportDelay()
    {
        if (config.MaxBaseTeleportDelaySeconds <= 0)
        {
            return true;
        }

        if (!memory.TryRemember<BaseTeleportDelayMemory>(out BaseTeleportDelayMemory delay))
        {
            delay = new BaseTeleportDelayMemory(BaseTeleportDelay.Roll(config));
            if (delay.Delay <= TimeSpan.Zero)
            {
                return true;
            }

            memory.TryAdd(delay);
            logger.Debug("Waiting {Seconds:F1}s at camp before teleport", delay.Delay.TotalSeconds);
            return false;
        }

        return delay.IsReady();
    }

    /// <summary>
    ///     Stay on foot for the short hop from pot preposition into a live pot FATE.
    ///     Mounting there (often ~25–35y) just delays hitting mobs.
    /// </summary>
    private bool ShouldAutoMountToward(Vector3 destination, IZone zone)
    {
        if (!movement.ShouldAutoMount)
        {
            return false;
        }

        if (objects.LocalPlayer is not { } player)
        {
            return true;
        }

        if (!memory.TryRemember<GoalMemory>(out GoalMemory goal)
            || goal.Goal.GoalType is not FateGoal fateGoal
            || !zone.IsPotFate(fateGoal.id.Value))
        {
            return true;
        }

        float toGoal = player.Position.Distance2D(destination);
        return toGoal > NavigationConstants.PotPrepositionMaxRadius * 1.5f;
    }

    private void BeginMountThenPause(string reason)
    {
        if (!movement.ShouldAutoMount || conditions[ConditionFlag.Mounted])
        {
            PauseForManualPathing(reason);
            return;
        }

        pendingPauseReason = reason;
        mountBeforePauseDeadline = DateTime.UtcNow + MountBeforePauseTimeout;
        if (!conditions[ConditionFlag.Mounting])
        {
            MountWait.TryCast(movement.PreferredMountId);
        }
    }

    /// <returns>True when this frame handled the mount-before-pause wait.</returns>
    private bool FinishMountBeforePause()
    {
        if (pendingPauseReason == null)
        {
            return false;
        }

        if (conditions[ConditionFlag.Mounted]
            || !movement.ShouldAutoMount
            || DateTime.UtcNow >= mountBeforePauseDeadline)
        {
            string reason = pendingPauseReason;
            pendingPauseReason = null;
            PauseForManualPathing(reason);
            return true;
        }

        if (!conditions[ConditionFlag.Mounting]
            && EzThrottler.Throttle("Pathfinding::MountBeforePause", 750))
        {
            MountWait.TryCast(movement.PreferredMountId);
        }

        return true;
    }

    private static string TeleportOnlyMessage(string where) =>
        $"Stop after return and teleport: {where} — paused so you can walk the rest "
        + "(Illegal Mode → Stop after return and teleport; toggle Illegal Mode to resume)";

    private void ReplanAfterPathCancel(string reason)
    {
        logger.Debug("{Reason} — dropping route for replan", reason);
        pathfinder.Stop();
        currentPathTask = null;
        pendingPauseReason = null;
        memory.Forget<GoalPathStepMemory>();
        memory.Forget<BaseTeleportDelayMemory>();
        // GoalMemory kept — Automator.Update rebuilds GoalPathStepMemory.
    }

    private void PauseForManualPathing(string reason)
    {
        logger.Info("{Reason} (toggle Illegal Mode to resume)", reason);
        pathfinder.Stop();
        ResetPathfinding();
        memory.Forget<GoalPathStepMemory>();
        memory.Forget<GoalMemory>();
        memory.Forget<BaseTeleportDelayMemory>();
        memory.TryAdd<NavigationInterruptedMemory>();
    }

    private void ResetPathfinding()
    {
        PathStepSoftStop.Cancel(manager);

        currentPathTask = null;
        pendingPauseReason = null;
        pathfinder.Stop();
    }
}
