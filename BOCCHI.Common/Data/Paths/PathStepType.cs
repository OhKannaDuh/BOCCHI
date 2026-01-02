using System.Numerics;

namespace BOCCHI.Common.Data.Paths;

public abstract record PathStepType;
public sealed record Teleport(uint id) : PathStepType;
public sealed record Pathfind(Vector3 destination) : PathStepType;
public sealed record Return: PathStepType;
