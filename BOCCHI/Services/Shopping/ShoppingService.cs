using BOCCHI.Common.Config;
using BOCCHI.Common.Data.Aethernet;
using BOCCHI.Common.Data.OccultCrescent;
using BOCCHI.Common.Data.StateMemory;
using BOCCHI.Common.Data.Zones;
using BOCCHI.Common.Services;
using BOCCHI.MobFarmer.Services;
using BOCCHI.Services.Shopping.Backends;
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
/// Return to central base camp if needed, then hand shopping to the configured backend
/// (GatherBuddy Reborn vendor list or Knightshopper's Occult Crescent list).
/// </summary>
public sealed class ShoppingService(
    ShoppingConfig config,
    IZoneProvider zones,
    ShoppingBackendSelector backends,
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
    /// When the backend cannot afford the next Occult Crescent buy, do not yank Illegal Mode
    /// back to camp every short cooldown — wait until farming can reasonably change balances.
    /// </summary>
    private static readonly TimeSpan InsufficientFundsCooldown = TimeSpan.FromMinutes(20);

    /// <summary>
    /// Backend missing, list missing / empty / fully stocked: re-check occasionally
    /// (an IPC query, no travel) rather than every tick.
    /// </summary>
    private static readonly TimeSpan NotReadyCooldown = TimeSpan.FromMinutes(2);

    private readonly CampReturnSession campReturn = new("Shopping::Return");

    private IMobFarmer Farmer => farmerFactory();

    /// <summary>Backend pinned for the current session so a mid-run config switch is safe.</summary>
    private IShoppingBackend? session;
    private bool priorityClaimed;
    private bool forcedSession;
    private DateTimeOffset buyCooldownUntil = DateTimeOffset.MinValue;

    private IShoppingBackend Backend => session ?? backends.Current;

    public bool IsActive =>
        priorityClaimed || session?.HasRun == true || campReturn.HasChain || forcedSession;

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
        AbortShopping(resumeAutomation: false, cancelRun: true);
    }

    /// <inheritdoc />
    public bool TryForceStart(out string detail)
    {
        IShoppingBackend backend = backends.Current;
        if (!backend.IsAvailable)
        {
            detail = $"{backend.Name} isn’t loaded or ready.";
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

        if (backend.CheckReady(out string reason) != ShoppingReadiness.Ready)
        {
            detail = reason;
            return false;
        }

        forcedSession = true;
        BeginSession(backend, zone);
        detail = zone.IsInBasecamp()
            ? $"At base camp — starting {backend.Name}."
            : $"Returning to base camp, then {backend.Name}.";
        return true;
    }

    /// <inheritdoc />
    public string DescribeStatus()
    {
        IShoppingBackend backend = Backend;
        string phase = session?.HasRun == true
            ? $"{backend.Name} running"
            : campReturn.HasChain
                ? "returning to base camp"
                : forcedSession || priorityClaimed
                    ? "preparing"
                    : "idle";

        IZone zone = zones.GetZone();
        Vector3 p = player.Position;
        return $"Shopping {phase} — zone={zone.ZoneId} camp={zone.IsInBasecamp()} "
            + $"forced={forcedSession} pos=<{p.X:0.##}, {p.Y:0.##}, {p.Z:0.##}> — {backend.DescribeStatus()}";
    }

    public void Update()
    {
        if (!config.EnableAutoShop && !forcedSession)
        {
            if (IsActive)
            {
                AbortShopping(resumeAutomation: true, cancelRun: true);
            }

            return;
        }

        IShoppingBackend backend = Backend;
        if (!backend.IsAvailable)
        {
            if (IsActive)
            {
                AbortShopping(resumeAutomation: true, cancelRun: false);
            }

            return;
        }

        IZone zone = zones.GetZone();
        if (!zone.IsOccultCrescentZone())
        {
            if (IsActive)
            {
                AbortShopping(resumeAutomation: true, cancelRun: true);
            }

            return;
        }

        if (backend.HasRun)
        {
            TickActiveRun(backend);
            return;
        }

        // Pending session (Returning, or backend said Retry at camp): TickReturn re-checks
        // FATE/CE and yields priority instead of holding it while waiting to start.
        if (campReturn.HasChain || (session is not null && priorityClaimed))
        {
            TickReturn(zone, backend);
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

        switch (backend.CheckReady(out string reason))
        {
            case ShoppingReadiness.Busy:
                return;
            case ShoppingReadiness.NotReady:
                logger.Debug(
                    "[Shopping] threshold hit but {Backend} not ready: {Reason} — recheck in {Minutes}m",
                    backend.Name,
                    reason,
                    NotReadyCooldown.TotalMinutes);
                buyCooldownUntil = DateTimeOffset.UtcNow + NotReadyCooldown;
                if (IsActive)
                {
                    AbortShopping(resumeAutomation: true, cancelRun: false);
                }

                return;
        }

        BeginSession(backend, zone);
    }

    private void BeginSession(IShoppingBackend backend, IZone zone)
    {
        session = backend;
        ClaimPriority();

        if (zone.IsInBasecamp())
        {
            TryStartBackend(backend);
            return;
        }

        TickReturn(zone, backend);
    }

    private void TickReturn(IZone zone, IShoppingBackend backend)
    {
        if (ShouldDeferForActivity())
        {
            logger.Debug("[Shopping] aborted — FATE/CE activity during Return");
            AbortShopping(resumeAutomation: true, cancelRun: true);
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
                TryStartBackend(backend);
                return;
            case CampReturnSession.TickResult.Failed:
                logger.Debug("[Shopping] Return unfinished — retrying");
                return;
            default:
                return;
        }
    }

    private void TryStartBackend(IShoppingBackend backend)
    {
        pathfinder.Stop();
        vnav.Stop();

        ShoppingStart start = backend.TryStart();
        switch (start.Kind)
        {
            case ShoppingStartKind.Started:
                ClaimPriority();
                logger.Debug("[Shopping] {Backend} started: {Message}", backend.Name, start.Message);
                return;
            case ShoppingStartKind.Rejected:
                // Empty list / not ready: back off so we do not spam Start every tick.
                logger.Debug("[Shopping] {Backend} did not start: {Message}", backend.Name, start.Message);
                buyCooldownUntil = DateTimeOffset.UtcNow + NotReadyCooldown;
                AbortShopping(resumeAutomation: true, cancelRun: false);
                return;
            default:
                // Busy / data still loading — keep session and retry next tick.
                logger.Debug("[Shopping] {Backend} not started yet: {Message}", backend.Name, start.Message);
                return;
        }
    }

    private void TickActiveRun(IShoppingBackend backend)
    {
        if (ShouldDeferForActivity())
        {
            logger.Debug("[Shopping] aborted — FATE/CE activity during {Backend} run", backend.Name);
            AbortShopping(resumeAutomation: true, cancelRun: true);
            return;
        }

        ShoppingPoll poll = backend.Poll();
        if (!poll.IsFinished)
        {
            ClaimPriority();
            return;
        }

        logger.Debug("[Shopping] {Backend} finished: {Message}", backend.Name, poll.Message);
        FinishShopping(poll.InsufficientFunds);
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
               || memory.TryRemember<CommittedFateMemory>(out CommittedFateMemory _)
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

    private void FinishShopping(bool insufficientFunds)
    {
        AbortShopping(resumeAutomation: true, cancelRun: false);

        TimeSpan cooldown = insufficientFunds ? InsufficientFundsCooldown : DefaultBuyCooldown;
        buyCooldownUntil = DateTimeOffset.UtcNow + cooldown;

        if (insufficientFunds)
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

    private void AbortShopping(bool resumeAutomation, bool cancelRun)
    {
        bool hadPriority = priorityClaimed;
        IShoppingBackend? backend = session;
        session = null;
        priorityClaimed = false;
        forcedSession = false;
        campReturn.Cancel(chainManager, pathfinder, vnav);
        pathfinder.Stop();
        vnav.Stop();

        if (cancelRun)
        {
            backend?.Cancel();
        }
        else
        {
            backend?.Forget();
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
