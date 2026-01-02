using BOCCHI.Common.Services;
using BOCCHI.Common.Steps;
using FFXIVClientStructs.FFXIV.Client.Game;
using Ocelot.Chain;

namespace BOCCHI.Services.Repair;

public class RepairService(IChainFactory chains) : IRepairService
{
    public unsafe bool ShouldRepair()
    {
        if (!TryGetEquipped(out var equipped))
        {
            return false;
        }

        for (var i = 0; i < equipped->Size; i++)
        {
            var item = equipped->GetInventorySlot(i);
            if (item is null)
            {
                continue;
            }

            //config.AutoRepairThreshold
            if (Convert.ToInt32(Convert.ToDouble(item->Condition) / 30000.0 * 100.0) <= 99)
            {
                return true;
            }
        }

        return false;
    }

    public IChain Repair()
    {
        var chain = chains.Create("Repairs");

        chain.Then<UnmountStep>();
        chain.Then<RepairStep>();

        return chain;
    }

    private static unsafe bool TryGetEquipped(out InventoryContainer* equipped)
    {
        equipped = null;

        var inventory = InventoryManager.Instance();
        if (inventory == null)
        {
            return false;
        }

        equipped = inventory->GetInventoryContainer(InventoryType.EquippedItems);
        if (equipped == null || !equipped->IsLoaded)
        {
            equipped = null;
            return false;
        }

        return true;
    }
}