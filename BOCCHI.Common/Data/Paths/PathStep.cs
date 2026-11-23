using BOCCHI.Common.Services.Paths;
using Ocelot.Chain;
using System.Numerics;

namespace BOCCHI.Common.Data.Paths;

public interface IPathStep
{
    PathStepType PathStepData { get; }

    PathStepKind Kind { get; init; }

    string Describe();

}

public abstract record PathStepType;
public sealed record Teleport(uint id) : PathStepType;
public sealed record Pathfind(Vector3 destination, float range) : PathStepType;
public sealed record Return : PathStepType;

public enum PathStepKind
{
    Teleport,
    Pathfind,
    Return
}

public class PathStep : IPathStep
{

    public required PathStepType PathStepData { get; init; }

    public required PathStepKind Kind { get; init; }

    public string Describe()
    {
        return PathStepData switch
        {
            Pathfind(var destination, var range) => $"Pathfind to {destination:f2} (range = {range:f2})",
            Teleport(var id) => $"Teleport to {id}",
            Return _ => "Return to Basecamp",
            var _ => throw new ArgumentOutOfRangeException(nameof(PathStepData))
        };
    }

    public static PathStep Teleport(uint id) =>
        new()
        {
            PathStepData = new Teleport(id),
            Kind = PathStepKind.Teleport
        };

    public static PathStep Pathfind(Vector3 destination, float range = 0f) =>
        new()
        {
            PathStepData = new Pathfind(destination, range),
            Kind = PathStepKind.Pathfind
        };

    public static PathStep Return() =>
        new()
        {
            PathStepData = new Return(),
            Kind = PathStepKind.Return
        };
}
