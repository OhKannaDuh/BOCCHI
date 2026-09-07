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

    bool IsOccultCrescentZone();

    bool IsInBasecamp();

    AethernetData GetMainAetheryte();

    Vector3 GetAetherytePosition();

    Vector3 GetStartingPosition();

    List<AethernetData> GetAetherytes();

    List<AethernetData> GetAethernetShards();

    List<KnowledgeCrystalData> GetNearbyKnowledgeCrystals();

    bool HasNearbyKnowledgeCrystals() => GetNearbyKnowledgeCrystals().Count != 0;

    /// <summary>
    ///     True when standing where crystal buffs can be cast: inside the authored buff radius
    ///     (including on the crystal), or ≤5y of a nearby knowledge crystal / shard crystal.
    /// </summary>
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

    /// <summary>
    ///     Authored knowledge-crystal centers away from camp (e.g. Forked Tower) so manual buff
    ///     works when the live object is filtered out or missing.
    /// </summary>
    List<Vector3> GetAuthoredKnowledgeCrystalCenters() => [];

    TreasureRoutePolicy GetTreasureRoutePolicy() => new();

    Task<ZoneGraph> GetGraph();

    /// <summary>Current path-map load lifecycle for UI / diagnostics.</summary>
    ZoneGraphLoadState GraphLoadState { get; }

    /// <summary>Where the ready path map came from (none until Ready).</summary>
    ZoneGraphSource GraphSource { get; }

    /// <summary>Drop the in-memory and on-disk zone path map so the next GetGraph rebuilds or reseeds.</summary>
    void InvalidateGraph(string? reason = null);

    /// <summary>Write live CE registration size onto the loaded path graph (approach / wait).</summary>
    void ApplyCriticalEncounterCombat(int eventId, float combatRadius, ActivityAreaShape shape);
}
