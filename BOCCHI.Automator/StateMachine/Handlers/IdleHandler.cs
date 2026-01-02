using BOCCHI.Automator.Data;
using BOCCHI.Automator.Data.StateMemory;
using BOCCHI.Automator.Services;
using BOCCHI.Common.Data.Zones;
using BOCCHI.Common.Services;
using Dalamud.Plugin.Services;
using Ocelot.Extensions;
using Ocelot.Pathfinding.Extensions;
using Ocelot.Services.Pathfinding;
using Ocelot.Services.UI;
using Ocelot.States.Score;

namespace BOCCHI.Automator.StateMachine.Handlers;

public class IdleHandler(
    IAutomatorMemory memory,
    IZoneProvider zones,
    IObjectTable objects,
    IPathfinder pathfinder,
    IUIService ui
) : ScoreStateHandler<AutomatorState, StatePriority>(AutomatorState.Idle)
{
    public override StatePriority GetScore()
    {
        return StatePriority.Lowest;
    }

    public override void Enter()
    {
        base.Enter();
        pathfinder.Stop();
        memory.TryAdd<IdleStateMemory>();
    }


    public override void Exit(AutomatorState next)
    {
        base.Exit(next);
        memory.Forget<IdleStateMemory>();
        pathfinder.Stop();
    }

    public override void Handle()
    {
        if (!pathfinder.IsIdle())
        {
            return;
        }

        var zone = zones.GetZone();
        if (!zone.IsInBasecamp())
        {
            return;
        }

        if (objects.LocalPlayer is not { } player)
        {
            return;
        }

        var aetheryte = zone.GetAetherytePosition();
        var distance = player.Position.Distance2D(aetheryte);
        const float maxInteractDistance = 2.5f;

        if (distance <= maxInteractDistance)
        {
            return;
        }

        var goal = aetheryte.GetApproachPosition(player.Position, maxInteractDistance, 30f);
        pathfinder.PathfindAndMoveTo(new PathfinderConfig(goal));
    }

    public override void Render()
    {
        base.Render();

        if (memory.TryRemember<IdleStateMemory>(out var idle))
        {
            ui.LabelledValue("Time Idle", idle.GetIdleTime().Format());
        }
    }
}
