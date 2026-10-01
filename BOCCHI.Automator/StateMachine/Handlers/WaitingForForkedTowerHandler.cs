using BOCCHI.Automator.Data;
using BOCCHI.Automator.Services.Goals;
using BOCCHI.Common;
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
using Ocelot.Services.Logger;
using Ocelot.Services.Pathfinding;
using Ocelot.Services.Translation;
using Ocelot.States.Score;
using Ocelot.Windows;
using System.Numerics;

namespace BOCCHI.Automator.StateMachine.Handlers;

/// <summary>
/// Parks on the Forked Tower pad until the tower teleports us in (or registration lapses).
/// Blood still needs the player to offer ciphers at the reliquary by hand — we only remind them.
/// </summary>
public class WaitingForForkedTowerHandler
(
    IAutomatorMemory memory,
    IObjectTable objects,
    ICondition conditions,
    IPathfinder pathfinder,
    IVNavmeshIpc vnav,
    IChainManager manager,
    IZoneProvider zones,
    IForkedTowerRegistration registration,
    AutomatorConfig config,
    UIConfig uiConfig,
    IChatGui chat,
    ITranslator<MainWindow> translator,
    ILogger<WaitingForForkedTowerHandler> logger
) : ScoreStateHandler<AutomatorState, StatePriority>(AutomatorState.WaitingForForkedTower)
{
    private const float ArrivalRadius = 6f;

    private const float ArrivalHeight = 10f;

    // Generous so walking over to the reliquary to offer ciphers does not drag us back.
    private const float LeashRadius = 25f;

    public override StatePriority GetScore()
    {
        if (!TryGetGoalTower(out CriticalEncounterId id, out Vector3 entrance)
            || objects.LocalPlayer is not { } player)
        {
            return StatePriority.Never;
        }

        if (memory.TryRemember<WaitingForForkedTowerMemory>(out WaitingForForkedTowerMemory wait) && wait.IsFor(id))
        {
            if (player.Position.Distance2D(entrance) > LeashRadius)
            {
                memory.Forget<WaitingForForkedTowerMemory>();
                return StatePriority.Never;
            }

            return StatePriority.VeryHigh;
        }

        return IsOnPad(player.Position, entrance) ? StatePriority.VeryHigh : StatePriority.Never;
    }

    public override void Enter()
    {
        base.Enter();
        memory.Forget<GoalPathStepMemory>();
        PathStepSoftStop.Stop(manager, pathfinder, vnav);

        if (!TryGetGoalTower(out CriticalEncounterId id, out _))
        {
            return;
        }

        if (memory.TryRemember<WaitingForForkedTowerMemory>(out WaitingForForkedTowerMemory wait) && wait.IsFor(id))
        {
            return;
        }

        memory.Forget<WaitingForForkedTowerMemory>();
        memory.TryAdd(new WaitingForForkedTowerMemory(id));
        logger.Info("Arrived at Forked Tower {Id} pad — waiting for teleport", id.Value);
    }

    public override void Handle()
    {
        if (!TryGetGoalTower(out CriticalEncounterId id, out _)
            || !memory.TryRemember<WaitingForForkedTowerMemory>(out WaitingForForkedTowerMemory wait)
            || !wait.IsFor(id))
        {
            return;
        }

        pathfinder.Stop();
        vnav.Stop();

        if (!config.StayMountedWhileWaitingForCe
            && conditions[ConditionFlag.Mounted]
            && EzThrottler.Throttle("WaitingForForkedTower::Unmount")
            && Actions.Unmount.CanCast())
        {
            Actions.Unmount.Cast();
        }

        if (!wait.ReminderPrinted)
        {
            wait.ReminderPrinted = true;
            bool needsCiphers = zones.GetZone().ZoneId == ZoneId.SouthHorn && !registration.HasRightOfEntry();
            BocchiChat.Print(
                chat,
                uiConfig,
                translator.T(needsCiphers
                    ? ".automation.automator.forked_tower_offer_ciphers"
                    : ".automation.automator.forked_tower_on_pad"));
        }
    }

    private static bool IsOnPad(Vector3 position, Vector3 entrance) =>
        position.Distance2D(entrance) <= ArrivalRadius
        && MathF.Abs(position.Y - entrance.Y) <= ArrivalHeight;

    private bool TryGetGoalTower(out CriticalEncounterId id, out Vector3 entrance)
    {
        id = default;
        entrance = default;

        if (!memory.TryRemember<GoalMemory>(out GoalMemory goal)
            || goal.Goal.GoalType is not ForkedTowerGoal tower
            || zones.GetZone().ForkedTowerEntrance is not { } pad)
        {
            return false;
        }

        id = tower.id;
        entrance = pad;
        return true;
    }
}
