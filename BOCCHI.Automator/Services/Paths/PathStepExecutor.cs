using BOCCHI.Automator.ChainRecipes;
using BOCCHI.Automator.Data.Paths;
using BOCCHI.Automator.Data.StateMemory;
using BOCCHI.Common.Data.Paths;
using BOCCHI.Common.Data.Zones;
using BOCCHI.Common.Services;
using BOCCHI.Common.Services.Paths;
using Dalamud.Plugin.Services;
using Ocelot.Actions;
using Ocelot.Chain;
using Ocelot.Chain.Extensions;
using Ocelot.Chain.Recipes;
using Ocelot.Extensions;
using Ocelot.Services.Pathfinding;

namespace BOCCHI.Automator.Services.Paths;

public class PathStepExecutor(
    IChainFactory chains,
    IChainManager manager,
    IObjectTable objects,
    IAutomatorMemory memory,
    IZoneProvider zone
) : IPathStepExecutor
{
    public Task<ChainResult> Execute(IPathStep step)
    {
        var chain = step.PathStepData switch
        {
            Pathfind(var destination) => chains.Create($"PathStep::Pathfind({destination:f2}")
                .Then(_ =>
                {
                    if (objects.LocalPlayer is not { } player)
                    {
                        return StepResult.Failure("Didn't mount");
                    }

                    var distance = player.Position.Distance(destination);
                    if (distance > 50f)
                    {
                        Actions.MountRoulette.Cast();
                    }

                    return StepResult.Success();
                }, "PathStep::MaybeMount")
                .Then<PathfindToChain, PathfinderConfig>(new PathfinderConfig(destination)),

            Teleport(var id) => chains.Create($"PathStep::Teleport({id}")
                .Then<TeleportToAethernetChain, uint>(id),


            Return _ => chains.Create($"PathStep::Return")
                .Then(_ => memory.TryAdd<ReturningStateMemory>(), "Remember to return")
                .WaitUntil(_ => new ValueTask<bool>(zone.GetZone().
                    IsInBasecamp()), TimeSpan.FromSeconds(10)),

            _ => throw new ArgumentOutOfRangeException(),
        };

        return manager.Manage(chain);
    }
}
