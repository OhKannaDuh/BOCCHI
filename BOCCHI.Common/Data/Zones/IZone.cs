using BOCCHI.Common.Data.Aethernet;
using BOCCHI.Common.Data.KnowledgeCrystals;
using BOCCHI.Common.Data.Zones.Graph;
using System.Numerics;

namespace BOCCHI.Common.Data.Zones;

public interface IZone
{
    ZoneId ZoneId { get; }

    ushort TerritoryType { get; }

    ushort ForkedTowerEventId { get; }

    /// <summary>Centre of the Forked Tower entry pad (Blood platform / Magic aetherial node); null when the zone has no tower.</summary>
    Vector3? ForkedTowerEntrance { get; }

    bool IsOccultCrescentZone();

    bool IsInBasecamp();

    AethernetData GetMainAetheryte();

    Vector3 GetAetherytePosition();

    Vector3 GetStartingPosition();

    List<AethernetData> GetAetherytes();

    List<AethernetData> GetAethernetShards();

    List<KnowledgeCrystalData> GetNearbyKnowledgeCrystals();

    bool HasNearbyKnowledgeCrystals() => GetNearbyKnowledgeCrystals().Count != 0;

    bool IsInBuffCastRange(Vector3 position)
    {
        if (GetBuffZone() is { } buffZone && buffZone.IsWithinCastRadius2D(position))
        {
            return true;
        }

        const float crystalInteractionRange = 5f;
        float maxSq = crystalInteractionRange * crystalInteractionRange;
        foreach (Vector3 site in GetAuthoredKnowledgeCrystalCenters())
        {
            float adx = position.X - site.X;
            float adz = position.Z - site.Z;
            if ((adx * adx) + (adz * adz) <= maxSq)
            {
                return true;
            }
        }

        foreach (KnowledgeCrystalData crystal in GetNearbyKnowledgeCrystals())
        {
            float dx = position.X - crystal.Position.X;
            float dz = position.Z - crystal.Position.Z;
            if ((dx * dx) + (dz * dz) <= maxSq)
            {
                return true;
            }
        }

        return false;
    }

    bool IsInForkedTower();

    bool IsPotFate(int fateId)
    {
        return GetPotFateData().Any(f => f.Id == fateId);
    }

    List<ActivityData> GetNormalFateData() => [];

    List<ActivityData> GetPotFateData() => [];

    List<ActivityData> GetCriticalEncounterData() => [];

    List<TreasureData> GetTreasureData() => [];

    Dictionary<int, List<PotChestData>> GetPotChestData() => [];

    List<PotChestData> GetRerollPotChestData() => [];

    List<CarrotData> GetCarrotData() => [];

    BuffZone? GetBuffZone() => null;

    List<Vector3> GetAuthoredKnowledgeCrystalCenters() => [];

    TreasureRoutePolicy GetTreasureRoutePolicy() => new();

    Task<ZoneGraph> GetGraph();

    ZoneGraphLoadState GraphLoadState { get; }

    ZoneGraphSource GraphSource { get; }

    void InvalidateGraph(string? reason = null);

    void ApplyCriticalEncounterCombat(int eventId, float combatRadius, ActivityAreaShape shape);
}
