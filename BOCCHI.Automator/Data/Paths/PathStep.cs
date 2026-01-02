using System.Numerics;
using BOCCHI.Automator.Services.Paths;
using BOCCHI.Common.Data.Paths;
using BOCCHI.Common.Services.Paths;
using Ocelot.Chain;

namespace BOCCHI.Automator.Data.Paths;

public class PathStep : IPathStep
{
    private Task<ChainResult>? task = null;

    public required PathStepType PathStepData { get; init; }

    public required PathStepKind Kind { get; init; }

    public string Describe()
    {
        return PathStepData switch
        {
            Pathfind(var destination) => $"Pathfind to {destination:f2}",
            Teleport(var id) => $"Teleport to {id}",
            Return _ => "Return to Basecamp",
            _ => throw new ArgumentOutOfRangeException(nameof(PathStepData))
        };
    }

    // Returns true when finished executing
    public bool TryExecute(IPathStepExecutor executor)
    {
        if (task != null)
        {
            if (task.IsCompleted)
            {
                task.Dispose();
                task = null;
                return true;
            }

            return false;
        }

        task = executor.Execute(this);
        return false;
    }

    public static PathStep Teleport(uint id)
    {
        return new PathStep
        {
            PathStepData = new Teleport(id),
            Kind = PathStepKind.Teleport,
        };
    }

    public static PathStep Pathfind(Vector3 destination)
    {
        return new PathStep
        {
            PathStepData = new Pathfind(destination),
            Kind = PathStepKind.Pathfind,
        };
    }

    public static PathStep Return()
    {
        return new PathStep
        {
            PathStepData = new Return(),
            Kind = PathStepKind.Return,
        };
    }
}
