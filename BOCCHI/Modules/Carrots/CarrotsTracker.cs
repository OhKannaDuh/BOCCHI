using System.Collections.Generic;
using System.Linq;
using BOCCHI.Enums;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Plugin.Services;
using ECommons.DalamudServices;
using ECommons.GameHelpers;

namespace BOCCHI.Modules.Carrots;

public class CarrotsTracker
{
    public List<Carrot> carrots = [];

    public void Tick(IFramework _)
    {
        carrots = Svc.Objects
            .Where(o => o.ObjectKind == ObjectKind.EventObj)
            .Where(o => o.BaseId == (uint)OccultObjectType.Carrot)
            .OrderBy(Player.DistanceTo)
            .Select(o => new Carrot(o))
            .Where(c => c.IsValid())
            .ToList();
    }
}
