using BOCCHI.Common.Data.Aethernet;
using System.Numerics;

namespace BOCCHI.Common.Data.Zones.Graph;

public interface INodeMetadata;
public class BlankNodeMetadata : INodeMetadata;

public class CarrotNodeMetadata : INodeMetadata
{
    public int Level { get; set; }
}

public class PotChestNodeMetadata : INodeMetadata
{
    public int FateId { get; set; }

    public int Level { get; set; }
}

public class RerollPotChestNodeMetadata : INodeMetadata
{
    public int Level { get; set; }
}

public class ActivityNodeMetadata : INodeMetadata
{
    public int Id { get; set; }

    public uint? PreferredAethernetId { get; set; }

    public float CombatRadius { get; set; }

    public ActivityAreaShape AreaShape { get; set; } = ActivityAreaShape.Circle;

    public float StandRadius { get; set; }
}

public class TeleportNodeMetadata : INodeMetadata
{
    public uint AetheryteId { get; set; } = 0;

    public Vector3 Destination { get; set; } = Vector3.Zero;

    public float DeadRadius { get; set; } = AethernetData.DefaultDeadRadius;
}

public enum TreasureType
{
    Silver,
    Bronze
}

public class TreasureNodeMetadata : INodeMetadata
{
    public TreasureType Type { get; set; }

    public int Level { get; set; }
}
