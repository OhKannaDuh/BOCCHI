using BOCCHI.Common.Config;
using BOCCHI.Common.Data.OccultCrescent;
using BOCCHI.Common.Data.StateMemory;
using BOCCHI.Common.Data.Zones;
using BOCCHI.Common.Ipc.Knightshopper;
using BOCCHI.Common.Services;
using BOCCHI.MobFarmer.Services;
using Ocelot.Lifecycle;
using Ocelot.Services.Logger;

namespace BOCCHI.Services.Shopping;

/// <summary>
/// When currency thresholds are hit, soft-suspend other automation and hand shopping to
/// Knightshopper (Occult Crescent list). No built-in travel/buy — KS handles that, including
/// from outside base camp.
/// </summary>
public sealed class ShoppingService(
    ShoppingConfig config,
    IZoneProvider zones,
    IKnightshopperIpc knightshopper,
    IAutomationModeGuard modeGuard,
    IFateContext fates,
    ICriticalEncounterContext criticalEncounters,
    IAutomatorMemory memory,
    Func<IMobFarmer> farmerFactory,
    ILogger<ShoppingService> logger
) : IShoppingService, IOnUpdate
{
    private IMobFarmer Farmer => farmerFactory();

    private Guid? operationId;
    private bool priorityClaimed;
    private DateTimeOffset buyCooldownUntil = DateTimeOffset.MinValue;

    public bool IsActive => priorityClaimed || operationId is not null;

    public UpdateLimit UpdateLimit =>
        new()
        {
            Mode = UpdateLimitMode.Milliseconds,
            Limit = 250
        };

    public void ForceStop()
    {
        if (!IsActive)
        {
            return;
        }

        logger.Debug("[Shopping] ForceStop");
        // Do not NotifyShoppingEnded here — Emergency Stop is mid-teardown (stopping=true)
        // and must not Resume hunts. Caller clears suspend / stops modes.
        AbortShopping(resumeAutomation: false, cancelKnightshopper: true);
    }

    public void Update()
    {
        if (!config.EnableAutoShop)
        {
            if (IsActive)
            {
                AbortShopping(resumeAutomation: true, cancelKnightshopper: true);
            }

            return;
        }

        if (!knightshopper.IsAvailable)
        {
            if (IsActive)
            {
                AbortShopping(resumeAutomation: true, cancelKnightshopper: false);
            }

            return;
        }

        IZone zone = zones.GetZone();
        if (!zone.IsOccultCrescentZone())
        {
            if (IsActive)
            {
                AbortShopping(resumeAutomation: true, cancelKnightshopper: true);
            }

            return;
        }

        if (operationId is { } activeId)
        {
            TickActiveOperation(activeId);
            return;
        }

        if (ShouldDeferForActivity())
        {
            return;
        }

        ZoneId zoneId = zone.ZoneId;
        int silver = OccultCrescentHelper.GetActiveSilver(zoneId);
        int gold = OccultCrescentHelper.GetActiveGold(zoneId);
        bool thresholdHit =
            (config.SilverThreshold > 0 && silver >= config.SilverThreshold)
            || (config.GoldThreshold > 0 && gold >= config.GoldThreshold);

        if (!thresholdHit || IsTriageActive() || IsMobFarmerBusy())
        {
            return;
        }

        if (DateTimeOffset.UtcNow < buyCooldownUntil)
        {
            return;
        }

        if (knightshopper.IsBusy)
        {
            return;
        }

        StartResponse start = knightshopper.Start(CurrencyId.OccultCrescent);
        if (!start.Started)
        {
            logger.Debug(
                "[Shopping] Knightshopper did not start: {Result} — {Message}",
                start.Result,
                start.Message);

            // Empty list / not ready: back off so we do not spam Start every tick.
            if (start.Result is StartResult.EmptyList or StartResult.NotReady or StartResult.NotLoggedIn
                or StartResult.InvalidCurrency)
            {
                buyCooldownUntil = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(30);
            }

            return;
        }

        operationId = start.OperationId;
        ClaimPriority();
        logger.Debug("[Shopping] Knightshopper started op={OperationId}", start.OperationId);
    }

    private void TickActiveOperation(Guid activeId)
    {
        if (ShouldDeferForActivity())
        {
            logger.Debug("[Shopping] aborted — FATE/CE activity during Knightshopper run");
            AbortShopping(resumeAutomation: true, cancelKnightshopper: true);
            return;
        }

        PurchaseStatus status = knightshopper.GetStatus(activeId);
        if (status.IsFinished)
        {
            logger.Debug(
                "[Shopping] Knightshopper finished: {State} — {Message}",
                status.State,
                status.Message);
            FinishShopping();
            return;
        }

        bool stillActive = knightshopper.IsRunning(activeId)
            || status.State is PurchaseState.Running or PurchaseState.CancellationRequested;
        if (!stillActive)
        {
            logger.Debug(
                "[Shopping] Knightshopper op lost: {State} — {Message}",
                status.State,
                status.Message);
            FinishShopping();
            return;
        }

        ClaimPriority();
    }

    private bool ShouldDeferForActivity()
    {
        if (fates.IsInFate()
            || criticalEncounters.IsInCriticalEncounter()
            || criticalEncounters.IsRegisteredOrInCriticalEncounter())
        {
            return true;
        }

        return memory.TryRemember<WaitingForCriticalEncounterMemory>(out WaitingForCriticalEncounterMemory _)
               || memory.TryRemember<CommittedCriticalEncounterMemory>(out CommittedCriticalEncounterMemory _)
               || memory.TryRemember<WaitingForPotFateMemory>(out WaitingForPotFateMemory _)
               || memory.TryRemember<SuspendTravelForActivityMemory>(out SuspendTravelForActivityMemory _)
               || memory.TryRemember<PotChestFarmMemory>(out PotChestFarmMemory _)
               || memory.TryRemember<PendingPotChestFarmMemory>(out PendingPotChestFarmMemory _);
    }

    private void ClaimPriority()
    {
        if (priorityClaimed)
        {
            return;
        }

        priorityClaimed = true;
        modeGuard.EnsureExclusive(AutomationMode.Shopping);
        logger.Debug("[Shopping] soft-suspended other automation for Knightshopper");
    }

    private void FinishShopping()
    {
        AbortShopping(resumeAutomation: true, cancelKnightshopper: false);
        buyCooldownUntil = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(30);
        logger.Debug("[Shopping] finished — cooldown 30s");
    }

    private void AbortShopping(bool resumeAutomation, bool cancelKnightshopper)
    {
        bool hadPriority = priorityClaimed;
        Guid? id = operationId;
        operationId = null;
        priorityClaimed = false;

        if (cancelKnightshopper && id is { } cancelId)
        {
            knightshopper.Cancel(cancelId);
        }

        if (hadPriority && resumeAutomation)
        {
            modeGuard.NotifyShoppingEnded();
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
}
