using Dalamud.Plugin.Services;
using ECommons.Throttlers;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;

namespace BOCCHI.Common.Services;

public static unsafe class InventoryItemAssist
{
    public static int Count(uint itemId)
    {
        InventoryManager* inventory = InventoryManager.Instance();
        return inventory == null ? 0 : inventory->GetInventoryItemCount(itemId, false);
    }

    public static bool Has(uint itemId, bool includeKeyItems = false)
    {
        if (Count(itemId) > 0)
        {
            return true;
        }

        return includeKeyItems && FindKeyItemSlot(itemId, out _, out _);
    }

    public static bool TryUse(
        uint itemId,
        string throttleKey,
        int throttleMs,
        IPluginLog log,
        string logPrefix,
        bool tryKeyItems = false)
    {
        if (!EzThrottler.Throttle(throttleKey, throttleMs))
        {
            return false;
        }

        if (!Has(itemId, tryKeyItems))
        {
            log.Warning("{Prefix}: item {Item} not in inventory", logPrefix, itemId);
            return false;
        }

        AgentInventoryContext* agent = AgentInventoryContext.Instance();
        if (agent == null)
        {
            log.Warning("{Prefix}: AgentInventoryContext unavailable", logPrefix);
            return false;
        }

        if (tryKeyItems
            && FindKeyItemSlot(itemId, out InventoryType type, out uint slot))
        {
            long keyResult = agent->UseItem(itemId, type, slot);
            if (keyResult is 0 or 1)
            {
                return true;
            }

            log.Warning(
                "{Prefix}: KeyItems UseItem({Item}) returned {Result} — trying default UseItem",
                logPrefix,
                itemId,
                keyResult);
        }

        long result = agent->UseItem(itemId);
        if (result is 0 or 1)
        {
            return true;
        }

        log.Warning("{Prefix}: UseItem({Item}) returned {Result}", logPrefix, itemId, result);
        return false;
    }

    private static bool FindKeyItemSlot(uint itemId, out InventoryType type, out uint slot)
    {
        type = InventoryType.KeyItems;
        slot = 0;

        InventoryManager* inventory = InventoryManager.Instance();
        InventoryContainer* keyItems = inventory != null
            ? inventory->GetInventoryContainer(InventoryType.KeyItems)
            : null;
        if (keyItems == null)
        {
            return false;
        }

        for (int i = 0; i < keyItems->Size; i++)
        {
            InventoryItem* item = keyItems->GetInventorySlot(i);
            if (item == null || item->IsEmpty() || item->ItemId != itemId)
            {
                continue;
            }

            slot = (uint)item->Slot;
            return true;
        }

        return false;
    }
}
