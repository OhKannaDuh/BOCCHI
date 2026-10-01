using BOCCHI.Common.Data.OccultCrescent;
using BOCCHI.Common.Data.Zones;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.InstanceContent;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;

namespace BOCCHI.Common.Services;

public static unsafe class OccultCrescentHelper
{
    public static OccultCrescentState* GetState() => PublicContentOccultCrescent.GetState();

    public static bool IsStateAvailable()
    {
        PublicContentOccultCrescent* instance = PublicContentOccultCrescent.GetInstance();
        return instance != null && instance->StateLoaded && GetState() != null;
    }

    public static int GetSilverPieces() => GetCurrencyCount(OccultCurrencies.SilverPieceItemId);

    public static int GetGoldPieces() => GetCurrencyCount(OccultCurrencies.GoldPieceItemId);

    private static int GetSilverObols() => GetCurrencyCount(OccultCurrencies.SilverObolItemId);

    private static int GetGoldObols() => GetCurrencyCount(OccultCurrencies.GoldObolItemId);

    public static int GetActiveSilver(ZoneId zone) =>
        zone == ZoneId.NorthHorn ? GetSilverObols() : GetSilverPieces();

    public static int GetActiveGold(ZoneId zone) =>
        zone == ZoneId.NorthHorn ? GetGoldObols() : GetGoldPieces();

    public static int GetCurrencyCount(uint itemId)
    {
        InventoryManager* inventory = InventoryManager.Instance();
        return inventory == null ? 0 : inventory->GetInventoryItemCount(itemId);
    }

    public static int GetSilverTotal() =>
        GetCurrencyCount(OccultCurrencies.SilverPieceItemId) + GetCurrencyCount(OccultCurrencies.SilverObolItemId);

    public static int GetGoldTotal() =>
        GetCurrencyCount(OccultCurrencies.GoldPieceItemId) + GetCurrencyCount(OccultCurrencies.GoldObolItemId);

    public static bool IsAethernetUnlocked(uint placeNameId)
    {
        AgentTelepotTown* agent = AgentModule.Instance() == null
            ? null
            : (AgentTelepotTown*)AgentModule.Instance()->GetAgentByInternalId(AgentId.TelepotTown);

        if (agent == null || agent->Data == null)
        {
            return true;
        }

        ref AgentTelepotTownData data = ref *agent->Data;
        int count = Math.Min((int)data.AetheryteCount, data.Entries.Length);
        for (var i = 0; i < count; i++)
        {
            AgentTelepotTownData.AetheryteEntry entry = data.Entries[i];
            if (entry.PlaceNameId == placeNameId)
            {
                return !entry.IsLocked && !entry.IsUnusable;
            }
        }

        return count == 0;
    }
}
