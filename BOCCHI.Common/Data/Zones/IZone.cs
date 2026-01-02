using System.Numerics;
using BOCCHI.Common.Data.Aethernet;
using BOCCHI.Common.Data.KnowledgeCrystals;

namespace BOCCHI.Common.Data.Zones;

public interface IZone
{
    bool IsOccultCrescentZone();

    bool IsInBasecamp();

    Vector3 GetAetherytePosition();

    Vector3 GetStartingPosition();

    List<AethernetData> GetAethernetShards();

    List<AethernetData> GetNearbyAethernetShards();

    bool HasNearbyAethernetShards()
    {
        return GetNearbyKnowledgeCrystals().Count != 0;
    }

    List<KnowledgeCrystalData> GetKnowledgeCrystals();

    List<KnowledgeCrystalData> GetNearbyKnowledgeCrystals();

    bool HasNearbyKnowledgeCrystals()
    {
        return GetNearbyKnowledgeCrystals().Count != 0;
    }

    bool IsInForkedTower();
}
