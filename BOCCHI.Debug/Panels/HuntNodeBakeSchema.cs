using BOCCHI.Treasure.Hunt;

namespace BOCCHI.Debug.Panels;

public sealed class HuntNodeBakeSchema
{
    public Dictionary<uint, List<BakeToNode>> NodeToNodeDistances { get; set; } = [];

    public Dictionary<HuntAethernet, List<BakeToNode>> AethernetToNodeDistances { get; set; } = [];

    public Dictionary<uint, List<BakeToAethernet>> NodeToAethernetDistances { get; set; } = [];
}

public sealed class BakeToNode(uint id, float distance, List<HuntPosition>? path)
{
    public uint Id { get; set; } = id;

    public float Distance { get; set; } = distance;

    public List<HuntPosition>? Path { get; set; } = path;
}

public sealed class BakeToAethernet(HuntAethernet aethernet, float distance, List<HuntPosition>? path)
{
    public HuntAethernet Aethernet { get; set; } = aethernet;

    public float Distance { get; set; } = distance;

    public List<HuntPosition>? Path { get; set; } = path;
}
