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
using System.Text.RegularExpressions;

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
    /// <summary>Normal pause between auto-shop attempts after a completed run.</summary>
    private static readonly TimeSpan DefaultBuyCooldown = TimeSpan.FromSeconds(30);

    /// <summary>
    /// When Knightshopper cannot afford the next Occult Crescent buy, do not yank Illegal Mode
    /// back to camp every short cooldown — wait until farming can reasonably change balances.
    /// </summary>
    private static readonly TimeSpan InsufficientFundsCooldown = TimeSpan.FromMinutes(20);

    private readonly CampReturnSession campReturn = new("Shopping::Return");

    private IMobFarmer Farmer => farmerFactory();

    private Guid? operationId;
    private bool priorityClaimed;
    private bool forcedSession;
    private DateTimeOffset buyCooldownUntil = DateTimeOffset.MinValue;

    public bool IsActive =>
        priorityClaimed || operationId is not null || campReturn.HasChain || forcedSession;

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
            : campReturn.HasChain
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

        if (campReturn.HasChain || (forcedSession && priorityClaimed && operationId is null))
        {
            TickReturn(zone);
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

        TickReturn(zone);
    }

    private void TickReturn(IZone zone)
    {
        if (ShouldDeferForActivity())
        {
            logger.Debug("[Shopping] aborted — FATE/CE activity during Return");
            AbortShopping(resumeAutomation: true, cancelKnightshopper: true);
            return;
        }

        ClaimPriority();
        CampReturnSession.TickResult result = campReturn.Tick(
            zone,
            player.Position,
            blockedFromReturn: conditions[ConditionFlag.InCombat] || conditions[ConditionFlag.Unconscious],
            zones,
            conditions,
            gui,
            pathfinder,
            vnav,
            chainManager,
            chains);

        switch (result)
        {
            case CampReturnSession.TickResult.Arrived:
                logger.Debug("[Shopping] Arrived at base camp");
                TryStartKnightshopper();
                return;
            case CampReturnSession.TickResult.Failed:
                logger.Debug("[Shopping] Return unfinished — retrying");
                return;
            default:
                return;
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
                buyCooldownUntil = DateTimeOffset.UtcNow + DefaultBuyCooldown;
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
            FinishShopping(status.Message);
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
            FinishShopping(status.Message);
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

    private void FinishShopping(string? finishMessage = null)
    {
        AbortShopping(resumeAutomation: true, cancelKnightshopper: false);

        TimeSpan cooldown = LooksLikeInsufficientFunds(finishMessage)
            ? InsufficientFundsCooldown
            : DefaultBuyCooldown;
        buyCooldownUntil = DateTimeOffset.UtcNow + cooldown;

        if (cooldown == InsufficientFundsCooldown)
        {
            logger.Debug(
                "[Shopping] finished — insufficient Occult Crescent for buy list, cooldown {Minutes}m",
                InsufficientFundsCooldown.TotalMinutes);
        }
        else
        {
            logger.Debug("[Shopping] finished — cooldown {Seconds}s", DefaultBuyCooldown.TotalSeconds);
        }
    }

    /// <summary>
    /// Knightshopper reports success with a message like
    /// "Insufficient OccultCrescent for … Need N, have M" when nothing was bought.
    /// Prefer the currency enum name + need/have counts (stable across UI languages);
    /// keep the English "Insufficient" wording as a fallback.
    /// </summary>
    private static bool LooksLikeInsufficientFunds(string? message)
    {
        if (string.IsNullOrEmpty(message))
        {
            return false;
        }

        if (message.Contains("Insufficient", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // CurrencyId.ToString() stays "OccultCrescent"; item name may be localized.
        if (!message.Contains(nameof(CurrencyId.OccultCrescent), StringComparison.Ordinal))
        {
            return false;
        }

        Match counts = NeedHaveCounts.Match(message);
        return counts.Success
               && long.TryParse(counts.Groups[1].Value, out long need)
               && long.TryParse(counts.Groups[2].Value, out long have)
               && need > have;
    }

    /// <summary>Trailing need/have integers from Knightshopper's unaffordable finish line.</summary>
    private static readonly Regex NeedHaveCounts = new(
        @"(\d+)\D+(\d+)\.?\s*$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private void AbortShopping(bool resumeAutomation, bool cancelKnightshopper)
    {
        bool hadPriority = priorityClaimed;
        Guid? id = operationId;
        operationId = null;
        priorityClaimed = false;
        forcedSession = false;
        campReturn.Cancel(chainManager, pathfinder, vnav);
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
