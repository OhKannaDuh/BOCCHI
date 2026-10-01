using System.Numerics;

namespace BOCCHI.Common.Data.Traps;

public sealed class TrapGroup(List<TrapDatum> traps)
{
    public IReadOnlyList<TrapDatum> Traps { get; } = traps;

    public float GetDistance2D(Vector3 from)
    {
        if (Traps.Count == 0)
        {
            return float.MaxValue;
        }

        float best = float.MaxValue;
        foreach (TrapDatum trap in Traps)
        {
            float dx = trap.Position.X - from.X;
            float dz = trap.Position.Z - from.Z;
            float dist = MathF.Sqrt((dx * dx) + (dz * dz));
            if (dist < best)
            {
                best = dist;
            }
        }

        return best;
    }
}

public sealed class TrapDatum(Vector3 position, uint type)
{
    public Vector3 Position { get; } = position;

    public uint Type { get; } = type;
}
