using BOCCHI.Common.Config;
using BOCCHI.Common.Data.Zones;
using Ocelot.Lifecycle;
using Ocelot.Windows;

namespace BOCCHI.Services;

public sealed class OccultCrescentWindowAutoOpener(
    UIConfig config,
    IZoneProvider zoneProvider,
    IEnumerable<IZone> zones,
    IMainWindow window
) : IOnStart, IOnTerritoryChanged
{
    private readonly Dictionary<ushort, IZone> zonesByTerritory = zones.ToDictionary(z => z.TerritoryType);

    public int Order => -100;

    public void OnStart()
    {
        OpenIfConfigured(zoneProvider.GetZone().IsOccultCrescentZone());
    }

    public void OnTerritoryChanged(uint territory)
    {
        OpenIfConfigured(
            territory <= ushort.MaxValue
            && zonesByTerritory.TryGetValue((ushort)territory, out IZone? zone)
            && zone.IsOccultCrescentZone());
    }

    private void OpenIfConfigured(bool isOccultCrescentZone)
    {
        if (!config.OpenOnOccultCrescentEntry || !isOccultCrescentZone || window.IsOpen)
        {
            return;
        }

        window.IsOpen = true;
    }
}
