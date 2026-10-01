using BOCCHI.Common.Data.EventDrops;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using Lumina.Excel.Sheets;
using MonsterNote = BOCCHI.Common.Data.EventDrops.MonsterNote;

namespace BOCCHI.Common.Services;

public sealed class FieldNoteTracker(IDataManager data, IUnlockState unlockState) : IFieldNoteTracker
{
    public bool HasNote(MonsterNote note)
    {
        uint itemId = (uint)note;
        if (GetInventoryCount(itemId) > 0)
        {
            return true;
        }

        if (!data.GetExcelSheet<Item>().TryGetRow(itemId, out Item item))
        {
            return false;
        }

        try
        {
            return unlockState.IsItemUnlocked(item);
        }
        catch
        {
            return false;
        }
    }

    public bool HasEntry(FieldNoteTargets.Entry entry)
    {
        if (IsMkdLoreUnlocked(entry.MkdLoreId))
        {
            return true;
        }

        return entry.Note is { } note && GetInventoryCount((uint)note) > 0;
    }

    public bool ShouldPursueFate(uint fateId)
    {
        if (!FieldNoteTargets.TryGetNoteForFate(fateId, out MonsterNote note))
        {
            return false;
        }

        return !HasNote(note);
    }

    public bool ShouldPursueCriticalEncounter(uint encounterId)
    {
        if (!FieldNoteTargets.TryGetNoteForCriticalEncounter(encounterId, out MonsterNote note))
        {
            return false;
        }

        return !HasNote(note);
    }

    private bool IsMkdLoreUnlocked(uint mkdLoreId)
    {
        if (!data.GetExcelSheet<MKDLore>().TryGetRow(mkdLoreId, out MKDLore row))
        {
            return false;
        }

        try
        {
            return unlockState.IsMKDLoreUnlocked(row);
        }
        catch
        {
            return false;
        }
    }

    private static unsafe int GetInventoryCount(uint itemId)
    {
        InventoryManager* inventory = InventoryManager.Instance();
        return inventory == null ? 0 : inventory->GetInventoryItemCount(itemId);
    }
}
