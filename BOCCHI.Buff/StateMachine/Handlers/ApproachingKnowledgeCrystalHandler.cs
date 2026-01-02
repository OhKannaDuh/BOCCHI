using BOCCHI.Buff.Data;
using BOCCHI.Common.Data.Zones;
using Ocelot.Extensions;
using Ocelot.Services.Logger;
using Ocelot.Services.Pathfinding;
using Ocelot.Services.PlayerState;
using Ocelot.States.Flow;

namespace BOCCHI.Buff.StateMachine.Handlers;

public class ApproachingKnowledgeCrystalHandler(
    IZoneProvider zones,
    IPlayer player,
    IPathfinder pathfinder,
    ILogger<ApproachingKnowledgeCrystalHandler> logger
) : FlowStateHandler<BuffState>(BuffState.ApproachingKnowledgeCrystal)
{
    private const float InteractionRange = 5f;

    public override BuffState? Handle()
    {
        var zone = zones.GetZone();
        if (!zone.IsOccultCrescentZone())
        {
            return null;
        }

        if (pathfinder.GetState() != PathfindingState.Idle)
        {
            return null;
        }

        var crystals = zone.GetNearbyKnowledgeCrystals().ToList();
        if (crystals.Count == 0)
        {
            return BuffState.NoCrystalsFound;
        }

        var closest = crystals.First();
        var distance = player.Position.Distance2D(closest.Position);
        if (distance <= InteractionRange)
        {
            return BuffState.ChoosingBuffToApply;
        }

        var destination = closest.Position.GetApproachPosition(player.Position, InteractionRange);
        pathfinder.PathfindAndMoveTo(new PathfinderConfig(destination)
        {
            ShouldSnapToFloor = true,
        });

        return null;
    }
}
