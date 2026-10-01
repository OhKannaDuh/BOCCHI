using System.Numerics;

namespace BOCCHI.Common.Data.Aethernet;

public class AethernetData
{
    public const float InteractRadius = 4.3f;

    public const float LifestreamEdgeClearance = 2.0f;

    public const float DefaultDeadRadius = 3.2f;

    public float DeadRadius { get; init; } = DefaultDeadRadius;

    public uint Id { get; init; }

    public uint BaseId { get; init; }

    public Vector3 Position { get; init; }

    public Vector3 Destination { get; init; }
}
