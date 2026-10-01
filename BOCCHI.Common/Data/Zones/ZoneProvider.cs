using BOCCHI.Common.Data.Aethernet;
using BOCCHI.Common.Data.KnowledgeCrystals;
using BOCCHI.Common.Data.Zones.Graph;
using Dalamud.Plugin.Services;
using System.Numerics;

namespace BOCCHI.Common.Data.Zones;

public interface IZoneProvider
{
    IZone GetZone();
}

public class ZoneProvider(IClientState client, IEnumerable<IZone> zones) : IZoneProvider
{
    private static readonly NullZone None = new();

    private readonly Dictionary<ushort, IZone> zoneMap = zones.ToDictionary(z => z.TerritoryType);

    public IZone GetZone() => zoneMap.TryGetValue((ushort)client.TerritoryType, out IZone? zone) ? zone : None;
}

public class NullZone : IZone
{
    public ZoneId ZoneId => ZoneId.Unknown;

    public ushort TerritoryType => 0;

    public bool IsOccultCrescentZone() => false;

    public bool IsInBasecamp() => false;

    public AethernetData GetMainAetheryte() => new();

    public Vector3 GetAetherytePosition() => Vector3.NaN;

    public Vector3 GetStartingPosition() => Vector3.NaN;

    public List<AethernetData> GetAetherytes() => [];

    public List<AethernetData> GetAethernetShards() => [];

    public List<KnowledgeCrystalData> GetNearbyKnowledgeCrystals() => [];

    public ushort ForkedTowerEventId => 0;

    public bool IsInForkedTower() => false;

    public Task<ZoneGraph> GetGraph() => Task.FromResult(new ZoneGraph());

    public ZoneGraphLoadState GraphLoadState => ZoneGraphLoadState.Idle;

    public ZoneGraphSource GraphSource => ZoneGraphSource.None;

    public void InvalidateGraph(string? reason = null)
    {
    }

    public void ApplyCriticalEncounterCombat(int eventId, float combatRadius, ActivityAreaShape shape)
    {
    }
}
