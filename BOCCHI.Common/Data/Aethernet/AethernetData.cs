using System.Numerics;

namespace BOCCHI.Common.Data.Aethernet;

public class AethernetData
{
    // It's actually 4.6, but this helps avoid issues
    public const float InteractRadius = 4.3f;

    public float DeadRadius { get; init; } = 3.2f;

    public uint Id { get; init; }

    public uint BaseId { get; init; }

    public Vector3 Position { get; init; }

    public Vector3 Destination { get; init; }
}
