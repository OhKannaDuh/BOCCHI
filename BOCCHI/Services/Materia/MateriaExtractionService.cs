using BOCCHI.Common.Services;
using BOCCHI.Common.Steps;
using FFXIVClientStructs.FFXIV.Client.Game;
using Ocelot.Chain;

namespace BOCCHI.Services.Materia;

public unsafe class MateriaExtractionService(IChainFactory chains) : IMateriaExtractionService
{
    public bool ShouldExtract()
    {
        if (!TryGetEquipped(out var equipped))
        {
            return false;
        }

        for (var i = 0; i < equipped->Size; i++)
        {
            var item = equipped->GetInventorySlot(i);
            if (item == null)
            {
                continue;
            }

            // 10000 is 100%
            if (item->SpiritbondOrCollectability >= 10000)
            {
                return true;
            }
        }

        return false;
    }

    public IChain ExtractEquipped()
    {
        var chain = chains.Create("Materia Extraction");

        chain.Then<UnmountStep>();
        chain.Then<ExtractStep>();

        return chain;
    }

    private static bool TryGetEquipped(out InventoryContainer* equipped)
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