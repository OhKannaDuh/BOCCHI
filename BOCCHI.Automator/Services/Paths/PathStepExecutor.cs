using BOCCHI.Common.Config;
using BOCCHI.Common.Data.Aethernet;
using BOCCHI.Common.Data.Paths;
using BOCCHI.Common.Data.Zones;
using BOCCHI.Common.Services.Paths;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using Ocelot.Chain;
using Ocelot.Chain.Extensions;
using Ocelot.Chain.Recipes;
using Ocelot.Extensions;
using Ocelot.Ipc.VNavmesh;
using Ocelot.Services.Pathfinding;

namespace BOCCHI.Automator.Services.Paths;

public class PathStepExecutor
(
    IChainFactory chains,
    IChainManager manager,
    IObjectTable objects,
    ICondition conditions,
    IZoneProvider zones,
    IGameGui gui,
    IPathfinder pathfinder,
    IVNavmeshIpc vnav,
    MovementConfig movement
) : IPathStepExecutor
{
    public Task<ChainResult> Execute(IPathStep step)
    {
        IChain chain = step.PathStepData switch
        {
            Pathfind(var destination, var range) => BuildPathfindChain(destination, range),

            Teleport(var id) => chains.Create($"{PathStepSoftStop.Prefix}Teleport({id})")
                .Then<AethernetTeleportChain, uint>(id),

            Return _ => ReturnToBaseCamp.Append(
                chains.Create($"{PathStepSoftStop.Prefix}Return"),
                zones,
                conditions,
                gui,
                pathfinder,
                vnav),

            var _ => throw new ArgumentOutOfRangeException()
        };

        return manager.Manage(chain);
    }

    private IChain BuildPathfindChain(System.Numerics.Vector3 destination, float range)
    {
        // Aetheryte stand-offs sit on a ring; floor-snap often jumps to the opposite pad.
        bool nearAetheryte = zones.GetZone().EnumerateAetherytes()
            .Any(a => destination.Distance2D(a.Position) <= a.GetIdleOuterRadius() + 3f);

        return chains.Create($"{PathStepSoftStop.Prefix}Pathfind({destination:f2}, {range:f2})")
            .Then<PathfindToChain, PathfinderConfig>(new(destination)
            {
                DistanceThreshold = range > 0f ? range : 2f,
                ShouldSnapToFloor = !nearAetheryte,
                WhileMoving = () =>
                {
                    IZone zone = zones.GetZone();
                    MountWait.TryCastIfNeeded(
                        conditions,
                        objects,
                        destination,
                        movement.ShouldAutoMount,
                        movement.PreferredMountId,
                        zone.IsInBasecamp(),
                        zone);
                },
            });
    }
}
