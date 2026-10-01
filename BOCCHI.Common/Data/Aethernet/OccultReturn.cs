using FFXIVClientStructs.FFXIV.Client.Game;
using Ocelot.Actions;

namespace BOCCHI.Common.Data.Aethernet;

public static class OccultReturn
{
    public static unsafe bool CanCast()
    {
        ActionManager* actions = ActionManager.Instance();
        return actions != null
               && actions->GetActionStatus(Actions.Return.Type, Actions.Return.Id) == 0;
    }

    public static bool Cast() => Actions.Return.Cast();
}
