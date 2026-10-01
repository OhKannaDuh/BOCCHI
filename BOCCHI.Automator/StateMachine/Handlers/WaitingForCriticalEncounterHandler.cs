using BOCCHI.Automator.Data;
using BOCCHI.Automator.Services;
using BOCCHI.Common.Config;
using BOCCHI.Common.Data.CriticalEncounters;
using BOCCHI.Common.Data.Goals;
using BOCCHI.Common.Data.StateMemory;
using BOCCHI.Common.Data.Zones;
using BOCCHI.Common.Services;
using BOCCHI.Common.Services.Paths;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using ECommons.Throttlers;
using Ocelot.Actions;
using Ocelot.Chain;
using Ocelot.Extensions;
using Ocelot.Ipc.VNavmesh;
using Ocelot.Services.Pathfinding;
using Ocelot.States.Score;

namespace BOCCHI.Automator.StateMachine.Handlers;

public class WaitingForCriticalEncounterHandler
(
    IAutomatorMemory memory,
    IObjectTable objects,
    ICondition conditions,
    IPathfinder pathfinder,
    IVNavmeshIpc vnav,
    IChainManager manager,
    ICriticalEncounterRepository repo,
    ICriticalEncounterContext context,
    AutomatorConfig config
) : ScoreStateHandler<AutomatorState, StatePriority>(AutomatorState.WaitingForCriticalEncounter)
{
    public override StatePriority GetScore()
    {
        if (!TryGetGoalEncounter(out CriticalEncounter ce))
        {
            return StatePriority.Never;
        }

        bool hasWaitLatch = memory.TryRemember<WaitingForCriticalEncounterMemory>(out WaitingForCriticalEncounterMemory wait)
                            && wait.IsFor(ce.Id);

        if (ce.IsActive())
        {
            if (ShouldHandOffToInCritical(ce))
            {
                return StatePriority.Never;
            }

            return hasWaitLatch ? StatePriority.VeryHigh : StatePriority.Never;
        }

        if (!ce.IsPreparing())
        {
            return StatePriority.Never;
        }

        // Already arrived — keep Waiting until outside the full red registration edge.
        // The inset wait disc is only for first arrival; using it here yanked you back to the
        // cyan stand as soon as you walked toward the rim.
        if (hasWaitLatch)
        {
            if (objects.LocalPlayer is { } latchedPlayer)
            {
                float latchedRadius = NavigationConstants.CriticalEncounterRedRadius(ce.Radius, ce.AreaShape);
                if (!NavigationConstants.IsInsideCriticalEncounterRegistrationArea(
                        ce.RegistrationCenter,
                        latchedRadius,
                        ce.AreaShape,
                        latchedPlayer.Position))
                {
                    memory.Forget<WaitingForCriticalEncounterMemory>();
                    return StatePriority.Never;
                }
            }

            return StatePriority.VeryHigh;
        }

        if (objects.LocalPlayer is not { } player)
        {
            return StatePriority.Never;
        }

        float combatRadius = NavigationConstants.CriticalEncounterRedRadius(ce.Radius, ce.AreaShape);
        if (!NavigationConstants.IsInsideCriticalEncounterWaitArea(
                ce.RegistrationCenter,
                combatRadius,
                ce.AreaShape,
                player.Position))
        {
            return StatePriority.Never;
        }

        return StatePriority.VeryHigh;
    }

    public override void Enter()
    {
        base.Enter();
        memory.Forget<GoalPathStepMemory>();
        PathStepSoftStop.Stop(manager, pathfinder, vnav);

        if (TryGetGoalEncounter(out CriticalEncounter ce))
        {
            memory.Forget<WaitingForCriticalEncounterMemory>();
            memory.TryAdd(new WaitingForCriticalEncounterMemory(ce.Id));
        }
    }

    public override void Handle()
    {
        if (!memory.TryRemember<WaitingForCriticalEncounterMemory>(out WaitingForCriticalEncounterMemory wait))
        {
            return;
        }

        if (!TryGetGoalEncounter(out CriticalEncounter ce))
        {
            return;
        }

        if (!wait.IsFor(ce.Id))
        {
            return;
        }

        if (!ce.IsActive() && !ce.IsPreparing())
        {
            return;
        }

        pathfinder.Stop();
        vnav.Stop();

        if (!config.StayMountedWhileWaitingForCe
            && conditions[ConditionFlag.Mounted]
            && EzThrottler.Throttle("WaitingForCriticalEncounter::Unmount")
            && Actions.Unmount.CanCast())
        {
            Actions.Unmount.Cast();
        }
    }

    private bool ShouldHandOffToInCritical(CriticalEncounter ce)
    {
        if (context.GetCriticalEncounterId() == ce.Id)
        {
            return true;
        }

        if (!memory.TryRemember<WaitingForCriticalEncounterMemory>(out WaitingForCriticalEncounterMemory wait)
            || !wait.IsFor(ce.Id)
            || objects.LocalPlayer is not { } player)
        {
            return false;
        }

        return CriticalEncounterBattleHandoff.IsReady(ce, context, player.Position);
    }

    private bool TryGetGoalEncounter(out CriticalEncounter ce)
    {
        ce = null!;

        if (!memory.TryRemember<GoalMemory>(out GoalMemory goal)
            || goal.Goal.GoalType is not CriticalEncounterGoal ceGoal)
        {
            return false;
        }

        CriticalEncounter? found = repo.SnapshotWithoutForkedTower().FirstOrDefault(c => c.Id == ceGoal.id);
        if (found is not { } encounter)
        {
            return false;
        }

        ce = encounter;
        return true;
    }
}
