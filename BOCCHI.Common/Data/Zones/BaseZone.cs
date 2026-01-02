using System.Numerics;
using BOCCHI.Common.Data.Aethernet;
using BOCCHI.Common.Data.KnowledgeCrystals;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.UI;

namespace BOCCHI.Common.Data.Zones;

public abstract class BaseZone(IObjectTable objects) : IZone
{
    protected abstract uint BasecampPlaceNmeId { get; }

    public bool IsOccultCrescentZone()
    {
        return true;
    }

    private unsafe uint GetCurrentSubAreaPlaceNameId()
    {
        var info = TerritoryInfo.Instance();
        return info == null ? 0 : info->SubAreaPlaceNameId;
    }

    public bool IsInBasecamp()
    {
        return GetCurrentSubAreaPlaceNameId() == BasecampPlaceNmeId;
    }

    public abstract Vector3 GetAetherytePosition();

    public abstract Vector3 GetStartingPosition();

    public virtual List<AethernetData> GetAethernetShards()
    {
        return [];
    }

    public virtual List<AethernetData> GetNearbyAethernetShards()
    {
        return [];
    }

    public virtual List<KnowledgeCrystalData> GetKnowledgeCrystals()
    {
        return [];
    }

    public List<KnowledgeCrystalData> GetNearbyKnowledgeCrystals()
    {
        return objects
            .Where(o => o.BaseId == KnowledgeCrystalData.BaseId)
            .Select(o => new KnowledgeCrystalData
            {
                Position = o.Position,
            })
            .ToList();
    }

    public bool IsInForkedTower()
    {
        throw new NotImplementedException();
    }
}
