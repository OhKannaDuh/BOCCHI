using ECommons;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace BOCCHI.Common.Services;

public static unsafe class AddonHelpers
{
    public static bool IsShopExchangeOpen() =>
        GenericHelpers.TryGetAddonByName("ShopExchangeCurrency", out AtkUnitBase* shop)
        && GenericHelpers.IsAddonReady(shop);

    public static bool TryGetSelectYesno(out AddonSelectYesno* yesno)
    {
        if (GenericHelpers.TryGetAddonByName("SelectYesno", out yesno)
            && yesno != null
            && GenericHelpers.IsAddonReady(&yesno->AtkUnitBase))
        {
            return true;
        }

        yesno = null;
        return false;
    }
}
