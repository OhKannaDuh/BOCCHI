using BOCCHI.Common.Config;
using BOCCHI.Common.Data.Aethernet;
using BOCCHI.Common.Data.OccultCrescent;
using BOCCHI.Common.Data.Shopping;
using BOCCHI.Common.Data.StateMemory;
using BOCCHI.Common.Data.SupportJobs;
using BOCCHI.Common.Data.Zones;
using BOCCHI.Common.Services;
using BOCCHI.MobFarmer.Services;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using ECommons;
using ECommons.Throttlers;
using ECommons.UIHelpers.AddonMasterImplementations;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Ocelot.Chain;
using Ocelot.Extensions;
using Ocelot.Ipc.VNavmesh;
using Ocelot.Lifecycle;
using Ocelot.Services.Logger;
using Ocelot.Services.PlayerState;
using System.Numerics;
using System.Runtime.InteropServices;

namespace BOCCHI.Services.Shopping;

/// <summary>
/// When currency thresholds are hit, soft-suspend other automation, visit the Expedition
/// Antiquarian, and buy from the shopping list. Never starts or continues travel during a
/// live FATE/CE, or while Mob Farmer is mid-pull / stacking / fighting.
/// </summary>
public sealed class ShoppingService
(
    ShoppingConfig config,
    IZoneProvider zones,
    IObjectTable objects,
    IPlayer player,
    IGameGui gui,
    IVNavmeshIpc vnav,
    IChainManager chainManager,
    IChainFactory chains,
    IAutomationModeGuard modeGuard,
    ISupportJobFactory supportJobs,
    IDataManager data,
    IUnlockState unlockState,
    IFateContext fates,
    ICriticalEncounterContext criticalEncounters,
    IAutomatorMemory memory,
    Func<IMobFarmer> farmerFactory,
    ILogger<ShoppingService> logger
) : IOnUpdate
{
    private IMobFarmer Farmer => farmerFactory();
    private enum Phase
    {
        Idle,
        Traveling,
        Approaching,
        OpeningMenu,
        Buying
    }

    private Phase phase = Phase.Idle;

    private void SetPhase(Phase next, string? detail = null)
    {
        if (phase == next)
        {
            return;
        }

        logger.Debug(
            "[Shopping] {Old} → {New}{Detail}",
            phase,
            next,
            string.IsNullOrEmpty(detail) ? string.Empty : $" ({detail})");
        phase = next;
    }
    private DateTimeOffset buyCooldownUntil = DateTimeOffset.MinValue;
    private bool priorityClaimed;
    private int desiredMenuIndex;
    private int? openedMenuIndex;
    private Task<ChainResult>? teleportChain;
    private readonly HashSet<uint> skippedMissingRows = [];
    private uint? tabHuntItemId;
    private int tabHuntAttempts;
    private bool tabHuntSettleGrace;

    private const float VendorInteractRange = 3.5f;

    /// <summary>Stop inside interact range — must be &lt; <see cref="VendorInteractRange"/>.</summary>
    private const float VendorPathArrival = 2f;

    private Vector3? approachTarget;

    public UpdateLimit UpdateLimit =>
        new()
        {
            Mode = UpdateLimitMode.Milliseconds,
            Limit = 250
        };

    public void Update()
    {
        if (!config.EnableAutoShop)
        {
            if (priorityClaimed || phase != Phase.Idle)
            {
                AbortShopping(resumeAutomation: true);
            }

            return;
        }

        IZone zone = zones.GetZone();
        if (!zone.IsOccultCrescentZone() || zone.GetShoppingVendor() is not { } vendor)
        {
            if (priorityClaimed || phase != Phase.Idle)
            {
                AbortShopping(resumeAutomation: true);
            }

            return;
        }

        ZoneId zoneId = zone.ZoneId;
        int silver = OccultCrescentHelper.GetActiveSilver(zoneId);
        int gold = OccultCrescentHelper.GetActiveGold(zoneId);
        bool thresholdHit =
            (config.SilverThreshold > 0 && silver >= config.SilverThreshold)
            || (config.GoldThreshold > 0 && gold >= config.GoldThreshold);

        // Never pull the player out of a live FATE/CE for shopping.
        if (IsInFateOrCriticalEncounter())
        {
            if (AddonHelpers.IsShopExchangeOpen() && (HasPendingGoals(zoneId) || phase == Phase.Buying))
            {
                // Already at the antiquarian with the shop open — finish buys or close.
                ClaimPriority();
                SetPhase(Phase.Buying);
                TryHandleOpenShop(zoneId);
                return;
            }

            if (phase != Phase.Idle || priorityClaimed)
            {
                AbortShopping(resumeAutomation: true);
                logger.Debug("[Shopping] aborted — in FATE/CE");
            }

            return;
        }

        if (AddonHelpers.IsShopExchangeOpen()
            && (HasPendingGoals(zoneId) || phase == Phase.Buying || priorityClaimed))
        {
            ClaimPriority();
            SetPhase(Phase.Buying);
            TryHandleOpenShop(zoneId);
            return;
        }

        // Threshold + goals: may interrupt treasure hunt / idle mob-farm waits, but not FATE/CE
        // or an active mob pull/fight.
        bool shouldShop =
            thresholdHit
            && HasPendingGoals(zoneId)
            && !IsTriageActive()
            && !IsMobFarmerBusy();

        if (!shouldShop && phase == Phase.Idle)
        {
            return;
        }

        if (!shouldShop && phase != Phase.Idle && !AddonHelpers.IsShopExchangeOpen())
        {
            // Threshold cleared mid-trip with nothing left to finish — resume.
            FinishShopping();
            return;
        }

        if (DateTimeOffset.UtcNow < buyCooldownUntil)
        {
            return;
        }

        ShopCatalogEntry? next = PickNextPurchase(zoneId, preferLiveRow: false);
        if (next == null)
        {
            if (phase != Phase.Idle)
            {
                FinishShopping();
            }

            return;
        }

        desiredMenuIndex = next.Value.MenuIndex;
        ClaimPriority();

        if (teleportChain != null)
        {
            TickTeleport();
            return;
        }

        IGameObject? npc = FindVendor(vendor.DataId);
        if (npc == null)
        {
            TryTravelToCamp(zone, vendor.PreferredAethernetId);
            return;
        }

        float distance = npc.Position.Distance2D(player.Position);
        if (distance > VendorInteractRange)
        {
            SetPhase(Phase.Approaching);
            if (vnav.IsNavmeshReady() && EzThrottler.Throttle("Shopping::Path", 1000))
            {
                // Path to the vendor (stable). A rotating GetApproachPosition stand-off near the
                // North camp crystal fought buff walks and never settled inside interact (#203).
                if (approachTarget is not { } held
                    || held.Distance2D(npc.Position) > VendorInteractRange)
                {
                    approachTarget = npc.Position;
                }

                vnav.PathfindAndMoveCloseTo(approachTarget.Value, false, VendorPathArrival);
            }

            return;
        }

        approachTarget = null;
        vnav.Stop();
        SetPhase(Phase.OpeningMenu);
        unsafe
        {
            if (gui.GetAddonByName("SelectIconString", 1).Address != nint.Zero)
            {
                TrySelectShopMenu(desiredMenuIndex);
                return;
            }

            if (EzThrottler.Throttle("Shopping::Interact", 1000))
            {
                openedMenuIndex = null;
                TargetSystem.Instance()->InteractWithObject(
                    (FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject*)npc.Address,
                    false);
            }
        }
    }

    private bool IsInFateOrCriticalEncounter() =>
        fates.IsInFate() || criticalEncounters.IsInCriticalEncounter();

    private void ClaimPriority()
    {
        if (priorityClaimed)
        {
            return;
        }

        priorityClaimed = true;
        modeGuard.EnsureExclusive(AutomationMode.Shopping);
        logger.Debug("[Shopping] soft-suspended other automation");
    }

    private void FinishShopping()
    {
        AbortShopping(resumeAutomation: true);
        buyCooldownUntil = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(30);
        logger.Debug("[Shopping] finished — nothing affordable left or trip complete");
    }

    private void AbortShopping(bool resumeAutomation)
    {
        SetPhase(Phase.Idle);
        openedMenuIndex = null;
        approachTarget = null;
        skippedMissingRows.Clear();
        ResetTabHunt();
        teleportChain = null;
        chainManager.CancelWhere(name => name.StartsWith("Shopping::", StringComparison.Ordinal));
        vnav.Stop();

        if (priorityClaimed && resumeAutomation)
        {
            priorityClaimed = false;
            modeGuard.NotifyShoppingEnded();
        }
        else if (!resumeAutomation)
        {
            priorityClaimed = false;
        }
    }

    private bool IsTriageActive() =>
        memory.TryRemember<PendingTriageMemory>(out PendingTriageMemory _)
        || memory.TryRemember<TriagingMemory>(out TriagingMemory _);

    /// <summary>
    /// Mob Farmer mid-pull / stack / fight — same window as other farmer yields.
    /// Suspended farmer (e.g. treasure) is not busy; shopping may take over.
    /// </summary>
    private bool IsMobFarmerBusy() =>
        Farmer.Running && !Farmer.Suspended && !Farmer.CanAcceptYield;

    private void TickTeleport()
    {
        SetPhase(Phase.Traveling);
        if (teleportChain is not { IsCompleted: true })
        {
            return;
        }

        bool ok = teleportChain.IsCompletedSuccessfully && (teleportChain.Result?.IsSuccess ?? false);
        teleportChain = null;
        if (!ok)
        {
            logger.Warn("[Shopping] aethernet to camp failed — will path if vendor is in range");
        }
    }

    private void TryTravelToCamp(IZone zone, uint preferredAethernetId)
    {
        SetPhase(Phase.Traveling);

        if (AetheryteApproach.IsAtPlaceName(zone, preferredAethernetId, player.Position)
            || zone.IsInBasecamp())
        {
            // At camp but vendor object not spawned yet — wait.
            return;
        }

        if (teleportChain != null)
        {
            return;
        }

        if (!EzThrottler.Throttle("Shopping::Teleport", 2000))
        {
            return;
        }

        vnav.Stop();
        teleportChain = chainManager.Manage(
            chains.Create($"Shopping::Teleport({preferredAethernetId})")
                .Then<AethernetTeleportChain, uint>(preferredAethernetId));
    }

    private IGameObject? FindVendor(uint dataId) =>
        objects
            .Where(o => o is { ObjectKind: ObjectKind.EventNpc, IsTargetable: true } && o.BaseId == dataId)
            .OrderBy(o => o.Position.Distance2D(player.Position))
            .FirstOrDefault();

    private unsafe bool TryHandleOpenShop(ZoneId zoneId)
    {
        if (!GenericHelpers.TryGetAddonByName("ShopExchangeCurrency", out AtkUnitBase* shop)
            || !GenericHelpers.IsAddonReady(shop))
        {
            return false;
        }

        // Confirm Yesno from a previous buy tick first.
        if (AddonHelpers.TryGetSelectYesno(out AddonSelectYesno* yesno))
        {
            if (EzThrottler.Throttle("Shopping::Yesno", 500))
            {
                try
                {
                    new AddonMaster.SelectYesno((nint)yesno).Yes();
                }
                catch
                {
                    // next tick retries
                }
            }

            return true;
        }

        ShopCatalogEntry? next = PickNextPurchase(zoneId, preferLiveRow: true);
        if (next == null)
        {
            ShopCatalogEntry? pending = PickNextPurchase(zoneId, preferLiveRow: false);
            // Same item in this shop at the wrong price (e.g. amulet Fixative vs silver Sink).
            if (pending is { } wrongPay
                && ShopExchangeAssist.TryGetListedOffer(
                    wrongPay.ItemId,
                    out _,
                    out uint listedCurrency,
                    out _)
                && listedCurrency != 0
                && listedCurrency != wrongPay.CurrencyItemId)
            {
                if (EzThrottler.Throttle("Shopping::CloseForCurrency", 1000))
                {
                    shop->FireCallbackInt(-1);
                    desiredMenuIndex = wrongPay.MenuIndex;
                    openedMenuIndex = null;
                    ResetTabHunt();
                    SetPhase(Phase.OpeningMenu);
                    logger.Debug(
                        $"[Shopping] {wrongPay.Name} listed for currency {listedCurrency}, want {wrongPay.CurrencyItemId} — switching menu {wrongPay.MenuIndex}");
                }

                return true;
            }

            // Same menu but wrong category tab (Armor vs Others, etc.), or another menu.
            if (pending is { } onMenu && onMenu.MenuIndex == openedMenuIndex)
            {
                if (TryCycleCategoryTab(shop, onMenu))
                {
                    return true;
                }

                skippedMissingRows.Add(onMenu.ItemId);
                ResetTabHunt();
                logger.Debug($"[Shopping] item {onMenu.Name} ({onMenu.ItemId}) not in any category tab — skip");
                return true;
            }

            if (pending is { } switchTo && switchTo.MenuIndex != openedMenuIndex)
            {
                if (EzThrottler.Throttle("Shopping::CloseForMenu", 1000))
                {
                    shop->FireCallbackInt(-1);
                    desiredMenuIndex = switchTo.MenuIndex;
                    openedMenuIndex = null;
                    ResetTabHunt();
                    SetPhase(Phase.OpeningMenu);
                    logger.Debug($"[Shopping] switching to menu {desiredMenuIndex}");
                }

                return true;
            }

            if (EzThrottler.Throttle("Shopping::Close", 2000))
            {
                shop->FireCallbackInt(-1);
                FinishShopping();
            }

            return true;
        }

        ShopCatalogEntry entry = next.Value;
        ResetTabHunt();
        if (!ShopExchangeAssist.TryGetListedOffer(entry.ItemId, out uint rowIndex, out uint liveCurrency, out uint liveCost))
        {
            skippedMissingRows.Add(entry.ItemId);
            logger.Debug($"[Shopping] item {entry.Name} ({entry.ItemId}) not in open shop — skip");
            return true;
        }

        if (liveCurrency != 0
            && (liveCurrency != entry.CurrencyItemId || liveCost != entry.Cost))
        {
            skippedMissingRows.Add(entry.ItemId);
            logger.Debug($"[Shopping] item {entry.Name} ({entry.ItemId}) not in open shop at expected cost — skip");
            return true;
        }

        if (!EzThrottler.Throttle("Shopping::Buy", 750))
        {
            return true;
        }

        logger.Debug($"[Shopping] buy item={entry.Name} ({entry.ItemId}) row={rowIndex} cost={entry.Cost}");
        FirePurchaseCallback(shop, rowIndex, 1);
        NotePurchase(entry.ItemId);
        buyCooldownUntil = DateTimeOffset.UtcNow + TimeSpan.FromMilliseconds(500);
        return true;
    }

    private void NotePurchase(uint itemId)
    {
        if (!config.Shopping.TryGetValue(itemId, out ShopListEntry? setting) || setting == null)
        {
            return;
        }

        if (setting.BuyAmount > 0)
        {
            setting.BuyAmount--;
        }
    }

    private bool HasPendingGoals(ZoneId zoneId)
    {
        foreach (uint itemId in config.ShoppingOrder)
        {
            if (!config.Shopping.TryGetValue(itemId, out ShopListEntry? setting) || setting == null)
            {
                continue;
            }

            // Only affordable preferred offers count — otherwise we keep soft-suspending
            // and retrying buys we already know will fail.
            if (!TryResolveAffordable(itemId, zoneId, setting, out ShopCatalogEntry entry))
            {
                continue;
            }

            if (ShopOwnership.ShouldBlockPurchase(entry, supportJobs, data, unlockState))
            {
                continue;
            }

            if (setting.BuyAmount > 0)
            {
                return true;
            }

            if (setting.KeepAmount > InventoryItemAssist.Count(itemId))
            {
                return true;
            }

            if (setting.KeepBuying)
            {
                return true;
            }
        }

        return false;
    }

    private ShopCatalogEntry? PickNextPurchase(ZoneId zoneId, bool preferLiveRow)
    {
        // Buy amounts first, then Keep stock-ups, then Keep Buying sink.
        return PickByGoal(zoneId, preferLiveRow, ShopGoal.Buy)
               ?? PickByGoal(zoneId, preferLiveRow, ShopGoal.Keep)
               ?? PickByGoal(zoneId, preferLiveRow, ShopGoal.KeepBuying);
    }

    private enum ShopGoal
    {
        Buy,
        Keep,
        KeepBuying,
    }

    private ShopCatalogEntry? PickByGoal(ZoneId zoneId, bool preferLiveRow, ShopGoal goal)
    {
        List<ShopCatalogEntry> candidates = [];
        foreach (uint itemId in config.ShoppingOrder)
        {
            if (!config.Shopping.TryGetValue(itemId, out ShopListEntry? setting) || setting == null)
            {
                continue;
            }

            if (!TryResolveAffordable(itemId, zoneId, setting, out ShopCatalogEntry entry))
            {
                continue;
            }

            if (entry.ItemId == 0 || skippedMissingRows.Contains(entry.ItemId))
            {
                continue;
            }

            if (ShopOwnership.ShouldBlockPurchase(entry, supportJobs, data, unlockState))
            {
                continue;
            }

            if (!MatchesGoal(setting, itemId, goal))
            {
                continue;
            }

            candidates.Add(entry);
        }

        if (candidates.Count == 0)
        {
            return null;
        }

        if (preferLiveRow)
        {
            // ItemId alone is not enough: Fixative (and others) list in silver, gold, and amulet
            // shops. Only buy when the open row's currency and cost match a Pay-with offer.
            foreach (ShopCatalogEntry entry in candidates)
            {
                if (!ShopExchangeAssist.TryGetListedOffer(
                        entry.ItemId,
                        out _,
                        out uint liveCurrency,
                        out uint liveCost))
                {
                    continue;
                }

                bool haveLiveCost = liveCurrency != 0 && liveCost != 0;
                if (haveLiveCost)
                {
                    if (liveCurrency != entry.CurrencyItemId || liveCost != entry.Cost)
                    {
                        continue;
                    }
                }
                else if (openedMenuIndex is not int opened || entry.MenuIndex != opened)
                {
                    // Can't read cost and we didn't open this menu — don't click the amulet listing.
                    continue;
                }

                if (openedMenuIndex is { } assumed && entry.MenuIndex != assumed)
                {
                    openedMenuIndex = entry.MenuIndex;
                }

                return entry;
            }

            return null;
        }

        if (openedMenuIndex is { } open)
        {
            foreach (ShopCatalogEntry entry in candidates)
            {
                if (entry.MenuIndex == open)
                {
                    return entry;
                }
            }
        }

        return candidates[0];
    }

    private static bool MatchesGoal(ShopListEntry setting, uint itemId, ShopGoal goal) =>
        goal switch
        {
            ShopGoal.Buy => setting.BuyAmount > 0,
            ShopGoal.Keep => setting.KeepAmount > InventoryItemAssist.Count(itemId),
            ShopGoal.KeepBuying => setting.KeepBuying,
            _ => false,
        };

    private bool TryResolveAffordable(
        uint itemId,
        ZoneId zoneId,
        ShopListEntry setting,
        out ShopCatalogEntry entry)
    {
        foreach (ShopCatalogEntry offer in ShopCatalog.PreferredOffers(
                     itemId, zoneId, setting.PreferredCurrencies))
        {
            if (CanAfford(offer))
            {
                entry = offer;
                return true;
            }
        }

        entry = default;
        return false;
    }

    private bool CanAfford(ShopCatalogEntry entry)
    {
        int have = OccultCrescentHelper.GetCurrencyCount(entry.CurrencyItemId);
        int reserve = 0;
        if (OccultCurrencies.IsSilverCurrency(entry.CurrencyItemId))
        {
            reserve = config.ReserveSilver;
        }
        else if (OccultCurrencies.IsGoldCurrency(entry.CurrencyItemId))
        {
            reserve = config.ReserveGold;
        }

        return have - reserve >= entry.Cost;
    }

    private unsafe void TrySelectShopMenu(int menuIndex)
    {
        if (!EzThrottler.Throttle("Shopping::SelectMenu", 750))
        {
            return;
        }

        try
        {
            nint addon = gui.GetAddonByName("SelectIconString", 1).Address;
            if (addon == nint.Zero)
            {
                return;
            }

            AddonMaster.SelectIconString master = new(addon);
            if (menuIndex < 0 || menuIndex >= master.Entries.Length)
            {
                logger.Warn($"[Shopping] menu index {menuIndex} out of range ({master.Entries.Length})");
                return;
            }

            master.Entries[menuIndex].Select();
            openedMenuIndex = menuIndex;
            skippedMissingRows.Clear();
            ResetTabHunt();
        }
        catch (Exception ex)
        {
            logger.Warn($"[Shopping] SelectIconString failed: {ex.Message}");
        }
    }

    private void ResetTabHunt()
    {
        tabHuntItemId = null;
        tabHuntAttempts = 0;
        tabHuntSettleGrace = false;
    }

    /// <summary>
    /// Cycle Weapons → Armor → Accessories → Others until AgentShop lists the item.
    /// </summary>
    private unsafe bool TryCycleCategoryTab(AtkUnitBase* shop, ShopCatalogEntry entry)
    {
        if (tabHuntItemId != entry.ItemId)
        {
            tabHuntItemId = entry.ItemId;
            tabHuntAttempts = 0;
            tabHuntSettleGrace = false;
        }

        if (tabHuntAttempts >= ShopExchangeAssist.CategoryTabCount)
        {
            // Last tab fire may need one more tick for AgentShop to refresh.
            if (!tabHuntSettleGrace)
            {
                tabHuntSettleGrace = true;
                return true;
            }

            return false;
        }

        if (!EzThrottler.Throttle("Shopping::ShopTab", 500))
        {
            return true;
        }

        int tabIndex = tabHuntAttempts;
        tabHuntAttempts++;
        logger.Debug($"[Shopping] category tab {tabIndex} for {entry.Name} ({entry.ItemId})");
        ShopExchangeAssist.TrySelectCategoryTab(shop, tabIndex);
        return true;
    }

    private static unsafe bool FirePurchaseCallback(AtkUnitBase* addon, uint rowIndex, int quantity)
    {
        AtkValue* values = (AtkValue*)Marshal.AllocHGlobal(4 * sizeof(AtkValue));
        if (values == null)
        {
            return false;
        }

        try
        {
            values[0] = default;
            values[1] = default;
            values[2] = default;
            values[3] = default;
            values[0].SetInt(0);
            values[1].SetUInt(rowIndex);
            values[2].SetInt(quantity);
            return addon->FireCallback(4, values, true);
        }
        finally
        {
            Marshal.FreeHGlobal((nint)values);
        }
    }
}
