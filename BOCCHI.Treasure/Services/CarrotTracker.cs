using BOCCHI.Common.Config;
using BOCCHI.Common.Data;
using BOCCHI.Common.Data.Zones;
using BOCCHI.Common.Services;
using BOCCHI.Treasure.Data;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Plugin.Services;
using Ocelot.Lifecycle;
using Ocelot.Services.PlayerState;
using System.Numerics;

namespace BOCCHI.Treasure.Services;

public interface ICarrotTracker
{
    IReadOnlyList<Carrot> Carrots { get; }
}

public class CarrotTracker(
    IObjectTable objects,
    IPlayer player,
    IZoneProvider zones,
    TreasureConfig config,
    Func<ICarrotHunter> carrotHunter
) : ICarrotTracker, IOnUpdate
{
    public IReadOnlyList<Carrot> Carrots { get; private set; } = [];

    public UpdateLimit UpdateLimit =>
        new()
        {
            Mode = UpdateLimitMode.Milliseconds,
            Limit = 250
        };

    public void Update()
    {
        if (!zones.GetZone().IsOccultCrescentZone())
        {
            if (Carrots.Count > 0)
            {
                Carrots = [];
            }

            return;
        }

        if (!config.DrawLineToCarrots && !carrotHunter().Running)
        {
            if (Carrots.Count > 0)
            {
                Carrots = [];
            }

            return;
        }

        if (player.PlayerCharacter == null)
        {
            Carrots = [];
            return;
        }

        Vector3 origin = player.Position;
        Carrots = objects
            .Where(o => o.ObjectKind == ObjectKind.EventObj)
            .Where(o => o.BaseId == OccultObjectType.Carrot)
            .OrderBy(o => Vector3.DistanceSquared(origin, o.Position))
            .Select(o => new Carrot(o))
            .Where(c => c.IsValid())
            .ToList();
    }
}
