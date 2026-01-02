using System.Numerics;
using BOCCHI.Common.Data.Aethernet;
using BOCCHI.Common.Data.KnowledgeCrystals;

namespace BOCCHI.Common.Data.Zones;

public class NullZone : IZone
{
    public bool IsOccultCrescentZone()
    {
        return false;
    }

    public bool IsInBasecamp()
    {
        return false;
    }

    public Vector3 GetAetherytePosition()
    {
        return Vector3.NaN;
    }

    public Vector3 GetStartingPosition()
    {
        return Vector3.NaN;
    }

    public List<AethernetData> GetAethernetShards()
    {
        return [];
    }

    public List<AethernetData> GetNearbyAethernetShards()
    {
        return [];
    }

    public List<KnowledgeCrystalData> GetKnowledgeCrystals()
    {
        return [];
    }

    public List<KnowledgeCrystalData> GetNearbyKnowledgeCrystals()
    {
        return [];
    }

    public bool IsInForkedTower()
    {
        return false;
    }
}
