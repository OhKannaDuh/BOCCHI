using BOCCHI.Common.Config;
using BOCCHI.Common.Data.Aethernet;
using BOCCHI.Common.Data.OccultCrescent;
using BOCCHI.Common.Data.StateMemory;
using BOCCHI.Common.Data.Zones;
using BOCCHI.Common.Ipc.Knightshopper;
using BOCCHI.Common.Services;
using BOCCHI.MobFarmer.Services;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using Ocelot.Chain;
using Ocelot.Ipc.VNavmesh;
using Ocelot.Lifecycle;
using Ocelot.Services.Logger;
using Ocelot.Services.Pathfinding;
using Ocelot.Services.PlayerState;
using System.Numerics;

namespace BOCCHI.Services.Shopping;

/// <summary>
/// When currency thresholds are hit (or debug force-start), soft-suspend other automation,
/// Return to central base camp if needed, then hand shopping to Knightshopper (Occult Crescent list).
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
    IChainManager chainManager,
    IChainFactory chains,
    ICondition conditions,
    IGameGui gui,
    IPathfinder pathfinder,
    IVNavmeshIpc vnav,
    IPlayer player,
    ILogger<ShoppingService> logger
) : IShoppingService, IOnUpdate
{
    private const string ReturnChainPrefix = "Shopping::Return";

    private IMobFarmer Farmer => farmerFactory();

    private Guid? operationId;
    private Task<ChainResult>? returnChain;
    private bool priorityClaimed;
    private bool forcedSession;
    private DateTimeOffset buyCooldownUntil = DateTimeOffset.MinValue;

    public bool IsActive =>
        priorityClaimed || operationId is not null || returnChain is not null || forcedSession;

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

    /// <inheritdoc />
    public bool TryForceStart(out string detail)
    {
        if (!knightshopper.IsAvailable)
        {
            detail = "Knightshopper IPC not available (plugin loaded?).";
            return false;
        }

        IZone zone = zones.GetZone();
        if (!zone.IsOccultCrescentZone())
        {
            detail = "Not in Occult Crescent.";
            return false;
        }

        if (IsActive)
        {
            detail = DescribeStatus();
            return false;
        }

        if (ShouldDeferForActivity())
        {
            detail = "Busy with FATE/CE / pot wait / pot-chest farm — try again when idle.";
            return false;
        }

        if (knightshopper.IsBusy)
        {
            detail = "Knightshopper is already busy.";
            return false;
        }

        forcedSession = true;
        ClaimPriority();
        BeginReturnOrShop(zone);
        detail = zone.IsInBasecamp()
            ? "At base camp — starting Knightshopper."
            : "Returning to base camp, then Knightshopper.";
        return true;
    }

    /// <inheritdoc />
    public string DescribeStatus()
    {
        string phase = operationId is not null
            ? "Knightshopper running"
            : returnChain is not null
                ? "returning to base camp"
                : forcedSession || priorityClaimed
                    ? "preparing"
                    : "idle";

        string ks = operationId is { } id
            ? FormatKnightshopperStatus(id)
            : $"available={knightshopper.IsAvailable} busy={knightshopper.IsBusy}";

        IZone zone = zones.GetZone();
        Vector3 p = player.Position;
        return $"Shopping {phase} — zone={zone.ZoneId} camp={zone.IsInBasecamp()} "
            + $"forced={forcedSession} pos=<{p.X:0.##}, {p.Y:0.##}, {p.Z:0.##}> — {ks}";
    }

    public void Update()
    {
        if (!config.EnableAutoShop && !forcedSession)
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

        if (returnChain is not null)
        {
            TickReturn(zone);
            return;
        }

        // Forced session already claimed priority but is waiting (e.g. combat walk-in).
        if (forcedSession && priorityClaimed)
        {
            BeginReturnOrShop(zone);
            return;
        }

        if (forcedSession)
        {
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

        ClaimPriority();
        BeginReturnOrShop(zone);
    }

    private void BeginReturnOrShop(IZone zone)
    {
        if (zone.IsInBasecamp())
        {
            TryStartKnightshopper();
            return;
        }

        if (conditions[ConditionFlag.InCombat] || conditions[ConditionFlag.Unconscious])
        {
            // Return cannot cast — walk toward camp until we can.
            EnsureWalkTowardCamp(zone);
            return;
        }

        if (returnChain is not null)
        {
            return;
        }

        pathfinder.Stop();
        vnav.Stop();
        logger.Debug("[Shopping] Returning to base camp before Knightshopper");
        returnChain = chainManager.Manage(
            ReturnToBaseCamp.Append(
                chains.Create(ReturnChainPrefix),
                zones,
                conditions,
                gui,
                pathfinder,
                vnav));
    }

    private void TickReturn(IZone zone)
    {
        if (returnChain is null)
        {
            return;
        }

        if (ShouldDeferForActivity())
        {
            logger.Debug("[Shopping] aborted — FATE/CE activity during Return");
            AbortShopping(resumeAutomation: true, cancelKnightshopper: true);
            return;
        }

        // Combat mid-Return: cancel chain and walk in.
        if (conditions[ConditionFlag.InCombat] || conditions[ConditionFlag.Unconscious])
        {
            CancelReturnChain();
            EnsureWalkTowardCamp(zone);
            return;
        }

        if (!returnChain.IsCompleted)
        {
            return;
        }

        bool ok = returnChain.IsCompletedSuccessfully
            && returnChain.Result.IsSuccess
            && zone.IsInBasecamp();
        string? error = returnChain.IsCompletedSuccessfully
            ? returnChain.Result.ErrorMessage
            : returnChain.Exception?.Message;
        returnChain = null;

        if (!ok)
        {
            logger.Debug(
                "[Shopping] Return unfinished ({Message}) — retrying",
                error ?? "?");
            BeginReturnOrShop(zone);
            return;
        }

        logger.Debug("[Shopping] Arrived at base camp");
        TryStartKnightshopper();
    }

    private void EnsureWalkTowardCamp(IZone zone)
    {
        ClaimPriority();
        Vector3 standOff = zone.GetMainAetheryte().GetCampStandOffPosition(player.Position);
        if (!vnav.IsRunning() && !vnav.IsPathfinding())
        {
            vnav.PathfindAndMoveCloseTo(standOff, false, AethernetNavigation.PathfindArrivalRadius);
        }
    }

    private void TryStartKnightshopper()
    {
        if (knightshopper.IsBusy)
        {
            logger.Debug("[Shopping] Knightshopper busy after Return — waiting");
            return;
        }

        pathfinder.Stop();
        vnav.Stop();

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
                AbortShopping(resumeAutomation: true, cancelKnightshopper: false);
                return;
            }

            // Busy / transient — keep session and retry next tick.
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
        logger.Debug("[Shopping] soft-suspended other automation for shopping");
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
        forcedSession = false;
        CancelReturnChain();
        pathfinder.Stop();
        vnav.Stop();

        if (cancelKnightshopper && id is { } cancelId)
        {
            knightshopper.Cancel(cancelId);
        }

        if (hadPriority && resumeAutomation)
        {
            modeGuard.NotifyShoppingEnded();
        }
    }

    private void CancelReturnChain()
    {
        if (returnChain is null)
        {
            return;
        }

        chainManager.CancelWhere(name => name.StartsWith(ReturnChainPrefix, StringComparison.Ordinal));
        returnChain = null;
    }

    private string FormatKnightshopperStatus(Guid id)
    {
        PurchaseStatus status = knightshopper.GetStatus(id);
        bool running = knightshopper.IsRunning(id);
        return $"op={id:N} running={running} state={status.State} "
            + $"item={status.CurrentIndex + 1}/{status.TotalItems} id={status.CurrentItemId} — {status.Message}";
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
