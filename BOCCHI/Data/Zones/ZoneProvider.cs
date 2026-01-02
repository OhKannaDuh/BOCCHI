using BOCCHI.Common.Data.Zones;
using Dalamud.Plugin.Services;

namespace BOCCHI.Data.Zones;

public class ZoneProvider(IClientState client, IObjectTable objects) : IZoneProvider
{
    private readonly Dictionary<ushort, IZone> Zones = new()
    {
        { 1252, new SouthHorn(objects) },
    };


    public IZone GetZone()
    {
        return Zones.TryGetValue(client.TerritoryType, out var zone) ? zone : new NullZone();
    }
}
