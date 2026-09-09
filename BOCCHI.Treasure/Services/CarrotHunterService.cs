using BOCCHI.Common;
using BOCCHI.Common.Config;
using BOCCHI.Common.Data;
using BOCCHI.Common.Data.Aethernet;
using BOCCHI.Common.Data.Zones;
using BOCCHI.Common.Data.Zones.Graph;
using BOCCHI.Common.Services;
using BOCCHI.Treasure.ChainRecipes;
using BOCCHI.Treasure.Data;
using BOCCHI.Treasure.Hunt;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using ECommons.Throttlers;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using Ocelot.Chain;
using Ocelot.Chain.Extensions;
using Ocelot.Extensions;
using Ocelot.Ipc.Lifestream;
using Ocelot.Ipc.VNavmesh;
using Ocelot.Lifecycle;
using Ocelot.Services.Pathfinding;
using Ocelot.Services.PlayerState;
using Ocelot.Services.Translation;
using Ocelot.Windows;
using System.Diagnostics;
using System.Numerics;
using DalamudObjectKind = Dalamud.Game.ClientState.Objects.Enums.ObjectKind;

namespace BOCCHI.Treasure.Services;

/// <summary>Authored carrot tour with aethernet hops, empty-pad skips, then Fortune Carrot → bunny.</summary>
public sealed class CarrotHunterService
(
    ICarrotTracker carrots,
    TreasureConfig treasureConfig,
    UIConfig uiConfig,
    MovementConfig movementConfig,
    IPlayer player,
    ICondition conditions,
    IObjectTable objects,
    IVNavmeshIpc vnav,
    IPathfinder pathfinder,
    IZoneProvider zones,
    IAutomationModeGuard modeGuard,
    IChainFactory chains,
    IChainManager chainManager,
    ILifestreamIpc lifestream,
    IGameGui gui,
    IChatGui chat,
    IPluginLog log,
    ITranslator<MainWindow> translator,
    PandoraAutoOpenHold pandoraAutoOpen,
    NinjaHideAssist ninjaHide,
    CarrotLocationSyncService carrotLocations
) : ICarrotHunter, IOnUpdate, IOnStop
{
    private const uint FortuneCarrotItemId = 48096;

    private const float BunnySearchRadius = 10f;

    private static readonly TimeSpan BunnySpawnTimeout = TimeSpan.FromSeconds(20);

    /// <summary>After UseItem is issued, wait this long for cast / inventory drop before retrying.</summary>
    private static readonly TimeSpan FortuneCarrotUseConfirmTimeout = TimeSpan.FromSeconds(2.5);

    private const int FortuneCarrotUseMaxAttempts = 3;

    private readonly WalkStuckWatch farStuckWatch = new(new WalkStuckWatch.Options(
        NudgeAfter: TimeSpan.FromSeconds(10),
        EscalateAfter: TimeSpan.FromSeconds(18),
        MaxEscalations: 2));

    private readonly EmptyPadConfirm emptyPadConfirm = new();

    private readonly CampReturnSession campReturn = new("CarrotHunt::Return");

    private const string FinishedRouteMessage = "Carrot Hunt finished the authored route.";

    private const string OutOfCarrotsMessage = "Out of Fortune Carrots — stopping Carrot Hunt.";

    private readonly Stopwatch stopwatch = new();

    private readonly HashSet<int> finishedAuthoredIds = [];

    private readonly HashSet<ulong> usedLiveCarrotIdsAtPad = [];

    private readonly List<CarrotData> tour = [];

    private int tourIndex;

    private CarrotData? currentAuthored;

    private ulong? currentLiveCarrotId;

    private Vector3 currentTargetPosition;

    private readonly List<Vector3> walkVias = [];

    private int walkViaIndex;

    private bool ninjaHideRequired;

    private readonly NinjaHideRouteGate ninjaHideRouteGate = new();

    private DateTime waitingForBunnySince = DateTime.MinValue;

    private bool itemUseIssued;

    private DateTime itemUseIssuedAt = DateTime.MinValue;

    private int fortuneCarrotCountAtIssue = -1;

    private int fortuneCarrotUseAttempts;

    private AethernetData? hopDeparture;

    private AethernetData? hopArrival;

    private Task<ChainResult>? activeTeleportChain;

    /// <summary>After Return succeeds: stop hunt (finish) vs continue to current authored pad.</summary>
    private bool returnThenStop;

    /// <summary>After mid-route Return, teleport from camp before walking to the pad.</summary>
    private bool returnThenAethernet;

    private float approachBestDistance = float.MaxValue;

    private DateTime approachLastProgressUtc = DateTime.MinValue;

    public bool Running { get; private set; }

    public bool Paused { get; private set; }

    public CarrotHuntPhase Phase { get; private set; } = CarrotHuntPhase.Idle;

    public TimeSpan Elapsed => stopwatch.Elapsed;

    public int FortuneCarrotsRemaining => InventoryItemAssist.Count(FortuneCarrotItemId);

    public bool IsVnavAvailable => vnav.IsAvailable();

    public bool IsVnavReady => vnav.IsNavmeshReady();

    public void OnStop()
    {
        Teardown();
    }

    public void Toggle()
    {
        if (Running)
        {
            Teardown();
            return;
        }

        if (!IsVnavAvailable || !IsVnavReady)
        {
            BocchiChat.PrintError(chat, uiConfig, "Carrot Hunt needs vnavmesh ready.");
            return;
        }

        if (!InventoryItemAssist.Has(FortuneCarrotItemId))
        {
            BocchiChat.PrintError(chat, uiConfig, "No Fortune Carrots in inventory.");
            return;
        }

        IZone zone = zones.GetZone();
        if (!zone.IsOccultCrescentZone() || carrotLocations.GetHuntPads(zone).Count == 0)
        {
            BocchiChat.PrintError(chat, uiConfig, "No authored carrot map for this zone.");
            return;
        }

        modeGuard.EnsureExclusive(AutomationMode.CarrotHunt);
        Running = true;
        Phase = CarrotHuntPhase.Idle;
        finishedAuthoredIds.Clear();
        ClearCurrent();
        stopwatch.Restart();
        pandoraAutoOpen.Hold();
        RecalculateAndAdvance();
        log.Information(
            "Carrot hunt started ({Kind}, {Count} spots)",
            zones.GetZone().ZoneId == ZoneId.NorthHorn ? "North Horn Middle→NW→NE" : "nearest-neighbor TSP",
            tour.Count);
    }

    public bool UseFortuneCarrot()
    {
        if (!InventoryItemAssist.Has(FortuneCarrotItemId))
        {
            BocchiChat.PrintError(chat, uiConfig, "No Fortune Carrots in inventory.");
            return false;
        }

        if (!TryUseFortuneCarrot(manual: true))
        {
            return false;
        }

        log.Debug("Carrot hunt: manual Fortune Carrot use");
        return true;
    }

    public void Pause()
    {
        if (!Running || Paused)
        {
            return;
        }

        Paused = true;
        SoftStopMovementForPause();
        stopwatch.Stop();
    }

    public void Resume()
    {
        if (!Running || !Paused)
        {
            return;
        }

        Paused = false;
        if (!stopwatch.IsRunning)
        {
            stopwatch.Start();
        }
    }

    public void Update()
    {
        if (!Running || Paused)
        {
            return;
        }

        if (!zones.GetZone().IsOccultCrescentZone())
        {
            StopDueToLeavingOccultCrescent();
            return;
        }

        if (!IsVnavReady)
        {
            vnav.Stop();
            return;
        }

        if (player.PlayerCharacter == null || player.IsBetweenAreas())
        {
            return;
        }

        if (conditions[ConditionFlag.Unconscious])
        {
            SoftStopWhileUnconscious();
            return;
        }

        switch (Phase)
        {
            case CarrotHuntPhase.Idle:
                TickIdle();
                break;
            case CarrotHuntPhase.ApproachingAetheryte:
                TickApproachingAetheryte();
                break;
            case CarrotHuntPhase.Teleporting:
                TickTeleporting();
                break;
            case CarrotHuntPhase.Returning:
                TickReturning();
                break;
            case CarrotHuntPhase.Pathing:
                TickPathing();
                break;
            case CarrotHuntPhase.UsingItem:
                TickUsingItem();
                break;
            case CarrotHuntPhase.WaitingForBunny:
                TickWaitingForBunny();
                break;
            case CarrotHuntPhase.OpeningBunny:
                TickOpeningBunny();
                break;
        }
    }

    private void TickIdle()
    {
        if (vnav.IsRunning())
        {
            vnav.Stop();
        }

        if (TryRestartLoop())
        {
            return;
        }

        if (treasureConfig.ReturnToBaseCampAfterHunt && !zones.GetZone().IsInBasecamp())
        {
            log.Information("Carrot hunt: route finished — returning to base camp");
            returnThenStop = true;
            returnThenAethernet = false;
            ClearHop();
            Phase = CarrotHuntPhase.Returning;
            return;
        }

        BocchiChat.Print(chat, uiConfig, FinishedRouteMessage);
        Teardown();
    }

    /// <summary>
    ///     Loop mode: a full empty pass means nothing left this wave — clear skips and
    ///     check every pad again for respawns. Stops when Fortune Carrots run out.
    /// </summary>
    private bool TryRestartLoop()
    {
        if (!treasureConfig.LoopCarrotHunt)
        {
            return false;
        }

        if (!InventoryItemAssist.Has(FortuneCarrotItemId))
        {
            BocchiChat.PrintError(chat, uiConfig, OutOfCarrotsMessage);
            return false;
        }

        finishedAuthoredIds.Clear();
        log.Information("Carrot hunt: full empty pass — rechecking all pads for respawns");
        RecalculateAndAdvance();
        return Phase != CarrotHuntPhase.Idle;
    }

    private void BeginRouteToCurrentAuthored()
    {
        ClearHop();
        returnThenStop = false;
        returnThenAethernet = false;
        campReturn.Detach();

        if (currentAuthored is not { } authored)
        {
            Phase = CarrotHuntPhase.Pathing;
            return;
        }

        IZone zone = zones.GetZone();
        List<AethernetData> aetherytes = zone.GetAetherytes();
        AethernetData main = zone.GetMainAetheryte();

        Vector3 destination = currentTargetPosition;
        float localDist = player.Position.Distance2D(destination);
        bool wrongFloor = !HuntDistances.IsSameFloor(player.Position, destination);
        // Keep Return for pad↔pad tour hops — and when 2D looks close but we are on the
        // wrong shelf (cliff / ridge). Direct then climbs into mesh ("underground").
        bool allowReturn = currentLiveCarrotId == null
            && (localDist > HuntDistances.NearbyLiveDivertRange || wrongFloor);

        HopMode mode = ChooseHopMode(
            player.Position,
            destination,
            aetherytes,
            main,
            out AethernetData? departure,
            out AethernetData? arrival,
            out _,
            allowReturn);

        switch (mode)
        {
            case HopMode.Return:
                Phase = CarrotHuntPhase.Returning;
                return;

            case HopMode.ReturnThenAethernet when arrival != null:
                hopDeparture = main;
                hopArrival = arrival;
                returnThenAethernet = true;
                Phase = CarrotHuntPhase.Returning;
                return;

            case HopMode.Aethernet when departure != null && arrival != null:
                if (AetheryteApproach.IsAlreadyAtAetheryte(arrival, player.Position))
                {
                    Phase = CarrotHuntPhase.Pathing;
                    return;
                }

                hopDeparture = departure;
                hopArrival = arrival;

                if (AetheryteApproach.IsReadyForLifestream(zone, lifestream, player.Position)
                    && AetheryteApproach.IsAlreadyAtAetheryte(departure, player.Position))
                {
                    Phase = CarrotHuntPhase.Teleporting;
                    return;
                }

                Phase = CarrotHuntPhase.ApproachingAetheryte;
                return;

            default:
                Phase = CarrotHuntPhase.Pathing;
                return;
        }
    }

    private void TickReturning()
    {
        if (!Running)
        {
            campReturn.Detach();
            return;
        }

        if (!returnThenStop && TryDivertToNearbyLiveCarrot())
        {
            return;
        }

        IZone zone = zones.GetZone();
        CampReturnSession.TickResult result = campReturn.Tick(
            zone,
            player.Position,
            blockedFromReturn: conditions[ConditionFlag.InCombat],
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
                OnReturnArrived();
                return;
            case CampReturnSession.TickResult.Failed:
                log.Warning("Carrot hunt: Return failed — walking instead");
                returnThenAethernet = false;
                ClearHop();
                if (returnThenStop)
                {
                    BocchiChat.Print(chat, uiConfig, FinishedRouteMessage);
                    Teardown();
                    return;
                }

                Phase = CarrotHuntPhase.Pathing;
                return;
            default:
                return;
        }
    }

    private void OnReturnArrived()
    {
        vnav.Stop();
        if (returnThenStop)
        {
            BocchiChat.Print(chat, uiConfig, FinishedRouteMessage);
            Teardown();
            return;
        }

        if (returnThenAethernet && hopArrival != null)
        {
            returnThenAethernet = false;
            hopDeparture = zones.GetZone().GetMainAetheryte();
            if (AetheryteApproach.IsAlreadyAtAetheryte(hopDeparture, player.Position))
            {
                Phase = CarrotHuntPhase.Teleporting;
                return;
            }

            Phase = CarrotHuntPhase.ApproachingAetheryte;
            return;
        }

        Phase = CarrotHuntPhase.Pathing;
    }

    private void TickApproachingAetheryte()
    {
        if (TryDivertToNearbyLiveCarrot())
        {
            return;
        }

        if (hopDeparture is not { } departure)
        {
            Phase = CarrotHuntPhase.Pathing;
            return;
        }

        IZone zone = zones.GetZone();
        Vector3 standOff = departure.GetCampStandOffPosition(player.Position);

        if (zone.IsWithinLifestreamRange(player.Position)
            || player.Position.Distance2D(standOff) <= AethernetNavigation.PathfindArrivalRadius + 0.35f)
        {
            vnav.Stop();
            Phase = CarrotHuntPhase.Teleporting;
            return;
        }

        if (!vnav.IsRunning() && !vnav.IsPathfinding())
        {
            vnav.PathfindAndMoveCloseTo(standOff, false, AethernetNavigation.PathfindArrivalRadius);
        }

        MaybeMount(standOff);
    }

    private void TickTeleporting()
    {
        if (hopArrival is { } arrival
            && AetheryteApproach.IsAlreadyAtAetheryte(arrival, player.Position))
        {
            activeTeleportChain = null;
            ClearHop();
            Phase = CarrotHuntPhase.Pathing;
            return;
        }

        if (hopDeparture is not { } departure)
        {
            ClearHop();
            Phase = CarrotHuntPhase.Pathing;
            return;
        }

        if (activeTeleportChain != null)
        {
            if (!activeTeleportChain.IsCompleted)
            {
                return;
            }

            bool teleported = activeTeleportChain.IsCompletedSuccessfully
                              && (activeTeleportChain.Result?.IsSuccess ?? false);
            activeTeleportChain = null;

            if (!teleported)
            {
                log.Warning(
                    "Carrot hunt: aethernet teleport to {Id} failed — walking instead",
                    hopArrival?.Id ?? 0);
                ClearHop();
                Phase = CarrotHuntPhase.Pathing;
                return;
            }

            ClearHop();
            Phase = CarrotHuntPhase.Pathing;
            return;
        }

        vnav.Stop();
        uint placeNameId = hopArrival?.Id ?? departure.Id;
        activeTeleportChain = chainManager.Manage(
            chains.Create($"CarrotHunt::Teleport({placeNameId})")
                .Then<AethernetTeleportChain, uint>(placeNameId));
    }

    private void TickPathing()
    {
        if (currentAuthored is not { } authored)
        {
            Phase = CarrotHuntPhase.Idle;
            return;
        }

        if (TryDivertToNearbyLiveCarrot())
        {
            return;
        }

        MaybeBindLiveCarrot(authored);

        if (currentLiveCarrotId == null)
        {
            SkipPassedWalkVias(authored.Position);
        }

        if (currentLiveCarrotId == null
            && walkViaIndex >= walkVias.Count
            && CanTrustEmptyCarrotPad(authored.Position)
            && ConfirmEmptyCarrotPad(authored.Id))
        {
            log.Debug(
                "Carrot hunt: no live carrot at authored {Id} — skipping",
                authored.Id);
            ClearEmptyPadCandidate();
            SkipCurrentAuthored();
            return;
        }

        if (TryGetCurrentLiveCarrot(out Carrot live))
        {
            ClearEmptyPadCandidate();
            currentTargetPosition = live.GetPosition();
        }
        else
        {
            currentTargetPosition = authored.Position;
            ResetApproachProgress();
        }

        float distTarget = player.Position.Distance2D(currentTargetPosition);
        if (MaybeDismountNear(distTarget))
        {
            return;
        }

        if (currentLiveCarrotId != null
            && (distTarget <= HuntDistances.UseRadius || IsStuckNearTarget(distTarget)))
        {
            ResetFarStuckWatch();
            vnav.Stop();
            bool keepNinja = StillThreatenedForRemount();
            if (!keepNinja)
            {
                ClearNinjaHideRequirement();
            }

            ninjaHide.EndStealthForInteract(keepNinja);
            Phase = CarrotHuntPhase.UsingItem;
            return;
        }

        if (currentLiveCarrotId == null && TryWalkVia(authored))
        {
            return;
        }

        if (TryRecoverFromStuckWalk(authored.Id, distTarget))
        {
            return;
        }

        TryNavigateToward(currentTargetPosition, OpenTreasureCofferChain.PathArrivalRange);
    }

    private void TickUsingItem()
    {
        MaintainNinjaHideDuringInteract();

        if (!TryGetCurrentLiveCarrot(out Carrot carrot))
        {
            SkipCurrentAuthored();
            return;
        }

        currentTargetPosition = carrot.GetPosition();

        if (player.IsCasting() || conditions[ConditionFlag.Casting])
        {
            // Cast in progress after UseItem — treat as confirmed and wait for the bunny.
            if (itemUseIssued)
            {
                ConfirmFortuneCarrotUseAndWait();
            }

            return;
        }

        float dist = player.Position.Distance2D(currentTargetPosition);
        if (MaybeDismountNear(dist))
        {
            return;
        }

        // Do not UseItem from stuck-outside-range — the agent can accept the call while the
        // game rejects the use (Ellie: "used" in log, no bunny, same pad later succeeds).
        if (dist > HuntDistances.UseRadius)
        {
            if (IsStuckNearTarget(dist))
            {
                TryNavigateToward(currentTargetPosition, HuntDistances.UseRadius);
            }
            else
            {
                Phase = CarrotHuntPhase.Pathing;
            }

            return;
        }

        if (itemUseIssued)
        {
            int remaining = InventoryItemAssist.Count(FortuneCarrotItemId);
            if (fortuneCarrotCountAtIssue >= 0 && remaining < fortuneCarrotCountAtIssue)
            {
                ConfirmFortuneCarrotUseAndWait();
                return;
            }

            if (DateTime.UtcNow - itemUseIssuedAt > FortuneCarrotUseConfirmTimeout)
            {
                log.Debug(
                    "Carrot hunt: Fortune Carrot use did not start at {Pos:F0} — retrying ({Attempt}/{Max})",
                    currentTargetPosition,
                    fortuneCarrotUseAttempts,
                    FortuneCarrotUseMaxAttempts);
                itemUseIssued = false;
                itemUseIssuedAt = DateTime.MinValue;
                fortuneCarrotCountAtIssue = -1;
            }

            return;
        }

        if (!InventoryItemAssist.Has(FortuneCarrotItemId))
        {
            BocchiChat.PrintError(chat, uiConfig, OutOfCarrotsMessage);
            Teardown();
            return;
        }

        if (fortuneCarrotUseAttempts >= FortuneCarrotUseMaxAttempts)
        {
            log.Warning(
                "Carrot hunt: Fortune Carrot use failed {Max} times at {Pos} — skipping",
                FortuneCarrotUseMaxAttempts,
                currentTargetPosition);
            SkipCurrentAuthored();
            return;
        }

        if (!TryUseFortuneCarrot())
        {
            return;
        }

        itemUseIssued = true;
        itemUseIssuedAt = DateTime.UtcNow;
        fortuneCarrotCountAtIssue = InventoryItemAssist.Count(FortuneCarrotItemId);
        fortuneCarrotUseAttempts++;
        log.Debug(
            "Carrot hunt: Fortune Carrot use issued at {Pos:F0} (attempt {Attempt})",
            currentTargetPosition,
            fortuneCarrotUseAttempts);
    }

    private void ConfirmFortuneCarrotUseAndWait()
    {
        waitingForBunnySince = DateTime.UtcNow;
        Phase = CarrotHuntPhase.WaitingForBunny;
        log.Debug("Carrot hunt: Fortune Carrot used at {Pos:F0}", currentTargetPosition);
    }

    private void TickWaitingForBunny()
    {
        if (player.IsCasting() || conditions[ConditionFlag.Casting])
        {
            return;
        }

        IGameObject? bunny = FindBunnyNear(currentTargetPosition);
        if (bunny != null)
        {
            log.Debug("Carrot hunt: bunny chest spawned near {Pos:F0}", bunny.Position);
            Phase = CarrotHuntPhase.OpeningBunny;
            return;
        }

        if (DateTime.UtcNow - waitingForBunnySince <= BunnySpawnTimeout)
        {
            return;
        }

        // Item never consumed — use was a false accept; retry at the pad instead of skipping.
        int remaining = InventoryItemAssist.Count(FortuneCarrotItemId);
        if (fortuneCarrotCountAtIssue >= 0 && remaining >= fortuneCarrotCountAtIssue)
        {
            log.Debug(
                "Carrot hunt: no bunny and Fortune Carrot still held at {Pos:F0} — retrying use",
                currentTargetPosition);
            itemUseIssued = false;
            itemUseIssuedAt = DateTime.MinValue;
            fortuneCarrotCountAtIssue = -1;
            waitingForBunnySince = DateTime.MinValue;
            Phase = CarrotHuntPhase.UsingItem;
            return;
        }

        log.Warning("Carrot hunt: no bunny chest near {Pos} — skipping", currentTargetPosition);
        SkipCurrentAuthored();
    }

    private void TickOpeningBunny()
    {
        MaintainNinjaHideDuringInteract();

        IGameObject? bunny = FindBunnyNear(currentTargetPosition);
        if (bunny == null)
        {
            // Bunny opened — stay if another chewed carrot shares this pad (double spawn).
            CompleteOrRebindSamePad();
            return;
        }

        float dist2d = player.Position.Distance2D(bunny.Position);
        float dist3d = player.Position.Distance(bunny.Position);

        // Path in until within open range (do not interact from 5–12y).
        if (dist3d > HuntDistances.BunnyInteractRadius
            && !(dist2d <= HuntDistances.StuckNearRadius && IsStuckNearTarget(dist2d)))
        {
            TryNavigateToward(bunny.Position, OpenTreasureCofferChain.PathArrivalRange);
            return;
        }

        if (vnav.IsRunning())
        {
            vnav.Stop();
            return;
        }

        // Bunny coffers need feet (same as pot reveals); hunt coffers stay mounted.
        if (DismountAssist.TryDismount(conditions))
        {
            return;
        }

        bool keepNinja = StillThreatenedForRemount();
        if (!keepNinja)
        {
            ClearNinjaHideRequirement();
        }

        ninjaHide.EndStealthForInteract(keepNinja);

        if (!EzThrottler.Throttle("CarrotHunt::InteractBunny", 400))
        {
            return;
        }

        unsafe
        {
            GameObject* gameObject = (GameObject*)(void*)bunny.Address;
            if (!gameObject->GetIsTargetable())
            {
                return;
            }

            TargetSystem.Instance()->InteractWithObject(gameObject, false);
        }
    }

    private bool TryBeginNextAuthored()
    {
        while (tourIndex < tour.Count)
        {
            CarrotData next = tour[tourIndex++];
            if (finishedAuthoredIds.Contains(next.Id))
            {
                continue;
            }

            currentAuthored = next;
            currentLiveCarrotId = null;
            currentTargetPosition = next.Position;
            LoadWalkVias(next);
            ResetApproachProgress();
            MaybeBindLiveCarrot(next);
            return true;
        }

        return false;
    }

    private void LoadWalkVias(CarrotData authored)
    {
        walkVias.Clear();
        walkViaIndex = 0;

        // West Suspended Masonry tip (~2.4, 35.9): vnav has no walkable jump link, so it routes
        // the long way around. Same on-mesh via as treasure 2061 (~3.4, 34.2).
        if (zones.GetZone().ZoneId == ZoneId.NorthHorn && authored.Id == 25)
        {
            walkVias.Add(new(-904f, 157.8f, 636f));
        }

        if (walkVias.Count == 0)
        {
            return;
        }

        SkipPassedWalkVias(authored.Position);
        if (walkViaIndex < walkVias.Count)
        {
            log.Debug(
                "Carrot hunt: {Count} approach via(s) for authored {Id}",
                walkVias.Count,
                authored.Id);
        }
    }

    private void ClearWalkVias()
    {
        walkVias.Clear();
        walkViaIndex = 0;
    }

    /// <summary>
    ///     Walk authored approach vias before the pad. Skips vias we are already on, and skips
    ///     the rest when already on the pad's floor closer to the carrot than to the via.
    /// </summary>
    private bool TryWalkVia(CarrotData authored)
    {
        SkipPassedWalkVias(authored.Position);
        if (walkViaIndex >= walkVias.Count)
        {
            return false;
        }

        Vector3 via = walkVias[walkViaIndex];
        const float viaArrival = 2.5f;
        float viaDist = player.Position.Distance2D(via);
        if (viaDist <= viaArrival)
        {
            walkViaIndex++;
            vnav.Stop();
            return walkViaIndex < walkVias.Count;
        }

        TryNavigateToward(via, viaArrival);
        return true;
    }

    private void SkipPassedWalkVias(Vector3 destination)
    {
        while (walkViaIndex < walkVias.Count)
        {
            Vector3 via = walkVias[walkViaIndex];
            if (player.Position.Distance2D(via) <= 3f)
            {
                walkViaIndex++;
                continue;
            }

            // Already on the island and closer to the carrot than this via — don't backtrack.
            if (HuntDistances.IsSameFloor(player.Position, destination)
                && player.Position.Distance2D(destination) <= player.Position.Distance2D(via))
            {
                walkViaIndex = walkVias.Count;
                return;
            }

            return;
        }
    }

    /// <summary>Re-solve nearest-neighbor tour on remaining pads, then begin the first hop.</summary>
    private void RecalculateAndAdvance(int? preferStartId = null)
    {
        ClearHop();
        campReturn.Detach();
        returnThenAethernet = false;
        returnThenStop = false;
        currentAuthored = null;
        currentLiveCarrotId = null;
        currentTargetPosition = Vector3.Zero;
        ClearWalkVias();
        itemUseIssued = false;
        itemUseIssuedAt = DateTime.MinValue;
        fortuneCarrotCountAtIssue = -1;
        fortuneCarrotUseAttempts = 0;
        waitingForBunnySince = DateTime.MinValue;
        ClearEmptyPadCandidate();
        ResetApproachProgress();
        ResetFarStuckWatch();
        usedLiveCarrotIdsAtPad.Clear();

        int? prefer = preferStartId ?? FindPreferredNextPadId();
        RebuildTour(prefer);
        if (tour.Count == 0)
        {
            Phase = CarrotHuntPhase.Idle;
            return;
        }

        if (!InventoryItemAssist.Has(FortuneCarrotItemId))
        {
            BocchiChat.PrintError(chat, uiConfig, OutOfCarrotsMessage);
            Teardown();
            return;
        }

        if (!TryBeginNextAuthored())
        {
            Phase = CarrotHuntPhase.Idle;
            return;
        }

        BeginRouteToCurrentAuthored();
        log.Debug(
            "Carrot hunt: nearest-neighbor replan ({Count} remaining, start {StartId})",
            tour.Count,
            currentAuthored?.Id ?? 0);
    }

    /// <summary>
    /// Prefer finishing the local cluster (cave / citadel) before hopping to distant live carrots.
    /// </summary>
    private int? FindPreferredNextPadId()
    {
        if (FindClosestUnfinishedInCluster(
                player.Position,
                HuntDistances.LocalClusterRadius,
                preferLive: true) is int localLive)
        {
            log.Debug("Carrot hunt preferring local live pad {Id}", localLive);
            return localLive;
        }

        if (FindClosestUnfinishedInCluster(
                player.Position,
                HuntDistances.LocalClusterRadius,
                preferLive: false) is int localPad)
        {
            log.Debug("Carrot hunt preferring local unfinished pad {Id}", localPad);
            return localPad;
        }

        return FindPreferredLiveNearbyPadId();
    }

    private int? FindClosestUnfinishedInCluster(Vector3 from, float radius, bool preferLive)
    {
        int? bestId = null;
        float bestDist = float.MaxValue;

        foreach (CarrotData pad in carrotLocations.GetHuntPads(zones.GetZone()))
        {
            if (finishedAuthoredIds.Contains(pad.Id))
            {
                continue;
            }

            if (!IsAllowedOnNorthHornTour(pad))
            {
                continue;
            }

            float dist = from.Distance2D(pad.Position);
            if (dist > radius)
            {
                continue;
            }

            if (preferLive && FindUnusedLiveCarrotNear(pad, HuntDistances.MatchRadiusSq) == null)
            {
                continue;
            }

            if (dist < bestDist)
            {
                bestDist = dist;
                bestId = pad.Id;
            }
        }

        return bestId;
    }

    private int? FindPreferredLiveNearbyPadId()
    {
        int? bestId = null;
        float bestDist = float.MaxValue;

        foreach (Carrot live in carrots.Carrots)
        {
            if (!live.IsValid() || usedLiveCarrotIdsAtPad.Contains(live.GameObjectId))
            {
                continue;
            }

            float dist = player.Position.Distance2D(live.GetPosition());
            if (dist > HuntDistances.NearbyLiveDivertRange)
            {
                continue;
            }

            CarrotData? pad = FindUnfinishedAuthoredPadForLive(live);
            if (pad == null || !IsAllowedOnNorthHornTour(pad))
            {
                continue;
            }

            if (dist < bestDist)
            {
                bestDist = dist;
                bestId = pad.Id;
            }
        }

        if (bestId is int id)
        {
            log.Debug(
                "Carrot hunt preferring live nearby pad {Id} at {Distance:F1}y",
                id,
                bestDist);
        }

        return bestId;
    }

    private void RebuildTour(int? preferStartId = null)
    {
        IZone zone = zones.GetZone();
        List<CarrotData> remaining = carrotLocations.GetHuntPads(zone)
            .Where(c => !finishedAuthoredIds.Contains(c.Id))
            .ToList();

        tour.Clear();
        tourIndex = 0;
        if (remaining.Count == 0)
        {
            return;
        }

        List<AethernetData> aetherytes = zone.GetAetherytes();
        AethernetData main = zone.GetMainAetheryte();
        Vector3 start = player.Position;

        if (NorthHornCarrotRegions.AppliesTo(zone.ZoneId))
        {
            RebuildNorthHornRegionTour(remaining, preferStartId, start, aetherytes, main);
            return;
        }

        AppendNearestNeighborTour(remaining, preferStartId, start, aetherytes, main);
        log.Debug(
            "Carrot hunt nearest-neighbor tour: {Count} remaining (start {Start})",
            tour.Count,
            tour[0].Id);
    }

    /// <summary>Walk the regions in TourOrder — death-zone babysitting stays one stretch.</summary>
    private void RebuildNorthHornRegionTour(
        List<CarrotData> remaining,
        int? preferStartId,
        Vector3 start,
        List<AethernetData> aetherytes,
        AethernetData main)
    {
        foreach (NorthHornCarrotRegion region in NorthHornCarrotRegions.TourOrder)
        {
            List<CarrotData> inRegion = remaining
                .Where(c => NorthHornCarrotRegions.Classify(c.Position) == region)
                .ToList();
            if (inRegion.Count == 0)
            {
                continue;
            }

            int? prefer = preferStartId is int id && inRegion.Any(c => c.Id == id)
                ? id
                : null;
            AppendNearestNeighborTour(inRegion, prefer, start, aetherytes, main);
            start = tour[^1].Position;
        }

        log.Debug(
            "Carrot hunt North Horn tour: {Count} remaining (start {Start}, {Order})",
            tour.Count,
            tour.Count > 0 ? tour[0].Id : 0,
            string.Join("→", NorthHornCarrotRegions.TourOrder));
    }

    private void AppendNearestNeighborTour(
        List<CarrotData> remaining,
        int? preferStartId,
        Vector3 start,
        List<AethernetData> aetherytes,
        AethernetData main)
    {
        CarrotData current = preferStartId is int prefId && remaining.Any(c => c.Id == prefId)
            ? remaining.First(c => c.Id == prefId)
            : PickCheapestStart(remaining, start, aetherytes, main);

        tour.Add(current);
        HashSet<int> unvisited = remaining.Select(c => c.Id).Where(id => id != current.Id).ToHashSet();
        Dictionary<int, CarrotData> byId = remaining.ToDictionary(c => c.Id);

        while (unvisited.Count > 0)
        {
            int? nearestId = PickNextTourPad(current.Position, unvisited, byId, aetherytes, main);
            if (nearestId is not int nextId)
            {
                break;
            }

            current = byId[nextId];
            tour.Add(current);
            unvisited.Remove(nextId);
        }
    }

    /// <summary>Clear the local cluster on foot before Return / aethernet hops to distant pads.</summary>
    private static int? PickNextTourPad(
        Vector3 from,
        HashSet<int> unvisited,
        Dictionary<int, CarrotData> byId,
        List<AethernetData> aetherytes,
        AethernetData main)
    {
        int? localId = null;
        float localBest = float.MaxValue;
        foreach (int id in unvisited)
        {
            float localDist = from.Distance2D(byId[id].Position);
            if (localDist > HuntDistances.LocalClusterRadius)
            {
                continue;
            }

            if (localDist < localBest)
            {
                localBest = localDist;
                localId = id;
            }
        }

        if (localId != null)
        {
            return localId;
        }

        int? nearestId = null;
        float best = float.MaxValue;
        foreach (int id in unvisited)
        {
            float d = TourCost(from, byId[id].Position, aetherytes, main, out _);
            if (d < best)
            {
                best = d;
                nearestId = id;
            }
        }

        return nearestId;
    }

    private CarrotData PickCheapestStart(
        List<CarrotData> remaining,
        Vector3 start,
        List<AethernetData> aetherytes,
        AethernetData main)
    {
        CarrotData best = remaining[0];
        float bestCost = float.MaxValue;
        foreach (CarrotData candidate in remaining)
        {
            float cost = TourCost(start, candidate.Position, aetherytes, main, out _);
            Carrot? liveNearPad = FindUnusedLiveCarrotNear(candidate, HuntDistances.MatchRadiusSq);
            if (liveNearPad != null)
            {
                float liveDist = start.Distance2D(liveNearPad.GetPosition());
                if (liveDist <= HuntDistances.NearbyLiveDivertRange)
                {
                    cost = Math.Min(cost, liveDist);
                }
            }

            if (cost < bestCost)
            {
                bestCost = cost;
                best = candidate;
            }
        }

        return best;
    }

    private enum HopMode
    {
        Direct,
        Aethernet,
        Return,
        ReturnThenAethernet
    }

    private static float TourCost(
        Vector3 from,
        Vector3 to,
        IReadOnlyList<AethernetData> aetherytes,
        AethernetData main,
        out HopMode mode)
    {
        mode = ChooseHopMode(from, to, aetherytes, main, out _, out _, out float cost, allowReturn: true);
        return cost;
    }

    private static HopMode ChooseHopMode(
        Vector3 from,
        Vector3 to,
        IReadOnlyList<AethernetData> aetherytes,
        AethernetData main,
        out AethernetData? departure,
        out AethernetData? arrival,
        out float bestCost,
        bool allowReturn = true)
    {
        departure = null;
        arrival = null;

        float directCost = from.Distance2D(to);
        bool directCrossesFloors = !HuntDistances.IsSameFloor(from, to);
        // 2D distance ignores cliffs — do not prefer Direct when the pad is on another shelf
        // until aethernet/Return have had a chance to win.
        bestCost = directCrossesFloors ? float.PositiveInfinity : directCost;
        HopMode bestMode = HopMode.Direct;

        float teleportCost = NavigationConstants.AethernetHopCost;
        float returnCost = NavigationConstants.ReturnCost;

        if (aetherytes.Count >= 2)
        {
            foreach (AethernetData shardA in aetherytes)
            {
                float toA = from.Distance2D(shardA.Position);
                foreach (AethernetData shardB in aetherytes)
                {
                    if (shardA.Id == shardB.Id || !IsUsableCarrotArrival(shardB, main))
                    {
                        continue;
                    }

                    float via = toA + teleportCost + shardB.Position.Distance2D(to);
                    if (via < bestCost)
                    {
                        bestCost = via;
                        bestMode = HopMode.Aethernet;
                        departure = shardA;
                        arrival = shardB;
                    }
                }
            }
        }

        if (!allowReturn)
        {
            if (float.IsPositiveInfinity(bestCost))
            {
                bestCost = directCost;
            }

            return bestMode;
        }

        float returnWalk = returnCost + main.Position.Distance2D(to);
        if (returnWalk < bestCost)
        {
            bestCost = returnWalk;
            bestMode = HopMode.Return;
            departure = null;
            arrival = null;
        }

        foreach (AethernetData shard in aetherytes)
        {
            if (shard.Id == main.Id || !IsUsableCarrotArrival(shard, main))
            {
                continue;
            }

            float via = returnCost + teleportCost + shard.Position.Distance2D(to);
            if (via < bestCost)
            {
                bestCost = via;
                bestMode = HopMode.ReturnThenAethernet;
                departure = main;
                arrival = shard;
            }
        }

        if (float.IsPositiveInfinity(bestCost))
        {
            bestCost = directCost;
        }

        return bestMode;
    }

    /// <summary>Lifestream landing pad — camp is always ok; locked field shards are not.</summary>
    private static bool IsUsableCarrotArrival(AethernetData shard, AethernetData main) =>
        shard.Id == main.Id || OccultCrescentHelper.IsAethernetUnlocked(shard.Id);

    private void MaybeBindLiveCarrot(CarrotData authored)
    {
        Carrot? live = FindUnusedLiveCarrotNear(authored, HuntDistances.MatchRadiusSq);
        currentLiveCarrotId = live?.GameObjectId;
        if (live != null)
        {
            currentTargetPosition = live.GetPosition();
        }
    }

    /// <summary>Divert to a nearer live chewed carrot (same pad rebind or other-pad replan).</summary>
    private bool TryDivertToNearbyLiveCarrot()
    {
        if (currentAuthored is not { } current)
        {
            return false;
        }

        float currentDist = player.Position.Distance2D(currentTargetPosition);
        if (currentDist < HuntDistances.NearbyLiveDivertMinCurrentDistance
            && currentLiveCarrotId != null)
        {
            return false;
        }

        Carrot? bestLive = null;
        CarrotData? bestPad = null;
        float bestDist = float.MaxValue;

        foreach (Carrot live in carrots.Carrots)
        {
            if (!live.IsValid() || usedLiveCarrotIdsAtPad.Contains(live.GameObjectId))
            {
                continue;
            }

            float distPlayer = player.Position.Distance2D(live.GetPosition());
            if (distPlayer > HuntDistances.NearbyLiveDivertRange)
            {
                continue;
            }

            CarrotData? pad = FindUnfinishedAuthoredPadForLive(live);
            if (pad == null || !IsAllowedOnNorthHornTour(pad, current))
            {
                continue;
            }

            if (distPlayer < bestDist)
            {
                bestDist = distPlayer;
                bestLive = live;
                bestPad = pad;
            }
        }

        if (bestLive == null || bestPad == null)
        {
            return false;
        }

        if (bestPad.Id == current.Id)
        {
            if (currentLiveCarrotId == bestLive.GameObjectId)
            {
                return false;
            }

            currentLiveCarrotId = bestLive.GameObjectId;
            currentTargetPosition = bestLive.GetPosition();
            CancelTravelForLocalCarrot();
            log.Debug(
                "Carrot hunt: rebinding to live carrot on authored {Id} at {Dist:F1}y",
                bestPad.Id,
                bestDist);
            return true;
        }

        if (bestDist + HuntDistances.NearbyLiveDivertClearAdvantage >= currentDist)
        {
            return false;
        }

        if (!EzThrottler.Throttle("CarrotHuntDivert", 8000))
        {
            return false;
        }

        log.Debug(
            "Carrot hunt: diverting to live carrot on authored {NearbyId} at {NearbyDist:F1}y (was {CurrentId} at {CurrentDist:F1}y)",
            bestPad.Id,
            bestDist,
            current.Id,
            currentDist);

        RecalculateAndAdvance(bestPad.Id);
        return true;
    }

    /// <summary>
    ///     North Horn: stay in the current region, or the first unfinished region in
    ///     <see cref="NorthHornCarrotRegions.TourOrder"/> when replanning.
    /// </summary>
    private bool IsAllowedOnNorthHornTour(CarrotData pad, CarrotData? currentPad = null)
    {
        if (!NorthHornCarrotRegions.AppliesTo(zones.GetZone().ZoneId))
        {
            return true;
        }

        if (currentPad != null)
        {
            return NorthHornCarrotRegions.Classify(pad.Position)
                   == NorthHornCarrotRegions.Classify(currentPad.Position);
        }

        NorthHornCarrotRegion? active = GetActiveNorthHornRegion();
        return active == null || NorthHornCarrotRegions.Classify(pad.Position) == active;
    }

    private NorthHornCarrotRegion? GetActiveNorthHornRegion()
    {
        NorthHornCarrotRegion? active = null;
        int activeOrder = int.MaxValue;
        foreach (CarrotData remaining in carrotLocations.GetHuntPads(zones.GetZone()))
        {
            if (finishedAuthoredIds.Contains(remaining.Id))
            {
                continue;
            }

            // Use TourIndex, not enum ordinal.
            NorthHornCarrotRegion region = NorthHornCarrotRegions.Classify(remaining.Position);
            int order = NorthHornCarrotRegions.TourIndex(region);
            if (order < activeOrder)
            {
                activeOrder = order;
                active = region;
            }
        }

        return active;
    }

    private CarrotData? FindUnfinishedAuthoredPadForLive(Carrot live)
    {
        Vector3 pos = live.GetPosition();
        float matchSq = HuntDistances.MatchRadiusSq;
        return carrotLocations.GetHuntPads(zones.GetZone())
            .Where(c => !finishedAuthoredIds.Contains(c.Id))
            .OrderBy(c => Vector3.DistanceSquared(c.Position, pos))
            .FirstOrDefault(c => Vector3.DistanceSquared(c.Position, pos) <= matchSq);
    }

    private void CancelTravelForLocalCarrot()
    {
        ClearHop();
        campReturn.Detach();
        returnThenAethernet = false;
        returnThenStop = false;
        if (Phase is CarrotHuntPhase.ApproachingAetheryte
            or CarrotHuntPhase.Teleporting
            or CarrotHuntPhase.Returning)
        {
            Phase = CarrotHuntPhase.Pathing;
        }

        vnav.Stop();
    }

    private bool CanTrustEmptyCarrotPad(Vector3 authoredPosition)
    {
        if (player.Position.Distance2D(authoredPosition) <= HuntDistances.EmptyPadSkipRadius)
        {
            return true;
        }

        float trustSq = HuntDistances.EmptyPadRegionTrustRadiusSq;
        return carrots.Carrots.Any(c =>
            c.IsValid()
            && Vector3.DistanceSquared(authoredPosition, c.GetPosition()) <= trustSq);
    }

    private bool ConfirmEmptyCarrotPad(int authoredId) =>
        emptyPadConfirm.Tick(authoredId, HuntDistances.EmptyPadConfirmDelay);

    private void ClearEmptyPadCandidate() => emptyPadConfirm.Clear();

    private Carrot? FindUnusedLiveCarrotNear(CarrotData authored, float matchRadiusSq)
    {
        return carrots.Carrots
            .Where(c => c.IsValid())
            .Where(c => !usedLiveCarrotIdsAtPad.Contains(c.GameObjectId))
            .Where(c => Vector3.DistanceSquared(authored.Position, c.GetPosition()) <= matchRadiusSq)
            .OrderBy(c => Vector3.DistanceSquared(authored.Position, c.GetPosition()))
            .FirstOrDefault();
    }

    private void CompleteOrRebindSamePad()
    {
        if (currentLiveCarrotId is { } usedId)
        {
            usedLiveCarrotIdsAtPad.Add(usedId);
        }

        itemUseIssued = false;
        itemUseIssuedAt = DateTime.MinValue;
        fortuneCarrotCountAtIssue = -1;
        fortuneCarrotUseAttempts = 0;
        currentLiveCarrotId = null;
        waitingForBunnySince = DateTime.MinValue;
        ResetApproachProgress();

        if (currentAuthored is { } authored)
        {
            Carrot? next = FindUnusedLiveCarrotNear(authored, HuntDistances.SamePadRecheckRadiusSq);
            if (next != null)
            {
                currentLiveCarrotId = next.GameObjectId;
                currentTargetPosition = next.GetPosition();
                log.Debug(
                    "Carrot hunt: another chewed carrot at authored {Id} — staying for double spawn",
                    authored.Id);
                Phase = CarrotHuntPhase.Pathing;
                return;
            }
        }

        CompleteCurrentAuthored();
    }

    private void SkipCurrentAuthored()
    {
        if (currentAuthored is { } authored)
        {
            finishedAuthoredIds.Add(authored.Id);
        }

        vnav.Stop();
        RecalculateAndAdvance();
    }

    private void CompleteCurrentAuthored()
    {
        if (currentAuthored is { } authored)
        {
            log.Debug("Carrot hunt: finished authored {Id} near {Pos:F0}", authored.Id, currentTargetPosition);
            if (treasureConfig.LoopCarrotHunt)
            {
                // Finding a carrot means others may have respawned — every pad must be checked again.
                finishedAuthoredIds.Clear();
                log.Debug("Carrot hunt: loop — cleared empty skips after using a carrot");
            }
            else
            {
                finishedAuthoredIds.Add(authored.Id);
            }
        }

        vnav.Stop();
        RecalculateAndAdvance();
    }

    private bool TryGetCurrentLiveCarrot(out Carrot carrot)
    {
        carrot = null!;
        if (currentLiveCarrotId is not { } id)
        {
            return false;
        }

        Carrot? match = carrots.Carrots.FirstOrDefault(c => c.IsValid() && c.GameObjectId == id);
        if (match == null)
        {
            // Live list may have refreshed — rebind from authored if still near.
            if (currentAuthored is { } authored)
            {
                MaybeBindLiveCarrot(authored);
                if (currentLiveCarrotId is { } rebound)
                {
                    match = carrots.Carrots.FirstOrDefault(c => c.IsValid() && c.GameObjectId == rebound);
                }
            }
        }

        if (match == null)
        {
            currentLiveCarrotId = null;
            return false;
        }

        carrot = match;
        return true;
    }

    private IGameObject? FindBunnyNear(Vector3 position)
    {
        return objects
            .Where(o => o is { ObjectKind: DalamudObjectKind.EventObj, IsDead: false } && o.IsValid())
            .Where(o => o.BaseId == OccultObjectType.BunnyChest)
            .OrderBy(o => Vector3.DistanceSquared(position, o.Position))
            .FirstOrDefault(o => Vector3.Distance(position, o.Position) <= BunnySearchRadius);
    }

    private void ClearHop()
    {
        hopDeparture = null;
        hopArrival = null;
        activeTeleportChain = null;
    }

    private void MaybeMount(Vector3 destination)
    {
        if (ninjaHideRequired)
        {
            return;
        }

        if (ninjaHide.IsStealthed
            && !ninjaHide.TryEndStealthForTravel(StillThreatenedForRemount))
        {
            return;
        }

        // Mount allowed in camp (matches treasure hunt).
        MountWait.TryCastIfNeeded(
            conditions,
            objects,
            destination,
            movementConfig.ShouldAutoMount,
            movementConfig.PreferredMountId,
            inBaseCamp: false);
    }

    /// <summary>Path/mount only after Hide is ready when required. Same gate as Treasure Hunt.</summary>
    private bool TryNavigateToward(Vector3 destination, float arrivalRadius)
    {
        if (!ApplyNinjaHideGate())
        {
            return false;
        }

        if (!vnav.IsRunning() && !vnav.IsPathfinding()
            && player.Position.Distance2D(destination) > arrivalRadius)
        {
            vnav.PathfindAndMoveCloseTo(destination, false, arrivalRadius);
        }

        MaybeMount(destination);
        return true;
    }

    /// <returns>False while still preparing Hide (caller should wait).</returns>
    private bool ApplyNinjaHideGate()
    {
        if (!treasureConfig.UseNinjaHideOnDangerousRoutes)
        {
            ClearNinjaHideRequirement();
            return true;
        }

        UpdateNinjaHideRequired();

        if (!ninjaHideRequired)
        {
            ninjaHide.RestorePreviousGearsetIfNeeded();
            return true;
        }

        if (conditions[ConditionFlag.InCombat])
        {
            return true;
        }

        if (ninjaHide.EnsureReady(treasureConfig.NinjaGearsetNumber))
        {
            if (treasureConfig.UseOccultSprintWhileHidden)
            {
                ninjaHide.TryOccultSprintWhileHidden();
            }

            return true;
        }

        if (treasureConfig.NinjaGearsetNumber <= 0 && !ninjaHide.IsNinja)
        {
            log.Warning(
                "Ninja Hide is on but gearset is 0 and you are not on Ninja — skipping Hide for this threat");
            ClearNinjaHideRequirement();
            return true;
        }

        vnav.Stop();
        pathfinder.Stop();
        return false;
    }

    private void UpdateNinjaHideRequired()
    {
        ninjaHideRequired = ninjaHideRouteGate.UpdateRequired(
            objects,
            player.Position,
            ninjaHideRequired,
            ninjaHide.IsMounted,
            treasureConfig.KnowledgeHideOffset,
            treasureConfig.KnowledgeThreatEnterDistance,
            treasureConfig.KnowledgeThreatExitDistance);
    }

    private bool StillThreatenedForRemount() =>
        ninjaHideRouteGate.ShouldKeepStealthForThreats(
            objects,
            player.Position,
            treasureConfig.KnowledgeHideOffset,
            treasureConfig.KnowledgeThreatExitDistance);

    private void ClearNinjaHideRequirement()
    {
        ninjaHideRequired = false;
        ninjaHideRouteGate.Reset();
    }

    private void MaintainNinjaHideDuringInteract()
    {
        if (!treasureConfig.UseNinjaHideOnDangerousRoutes || !ninjaHideRequired)
        {
            return;
        }

        if (conditions[ConditionFlag.InCombat])
        {
            return;
        }

        _ = ninjaHide.EnsureReady(treasureConfig.NinjaGearsetNumber);
    }

    private bool TryRecoverFromStuckWalk(int authoredId, float distance)
    {
        // Near-target stuck is handled by IsStuckNearTarget (interact from here).
        if (distance <= HuntDistances.StuckNearRadius)
        {
            ResetFarStuckWatch();
            return false;
        }

        switch (farStuckWatch.Tick(authoredId, distance))
        {
            case WalkStuckWatch.Action.Nudge:
                TryIssueStuckNudge();
                return true;
            case WalkStuckWatch.Action.GiveUp:
                log.Information(
                    "Carrot hunt: giving up on authored {Id} after stuck recoveries",
                    authoredId);
                SkipCurrentAuthored();
                return true;
            case WalkStuckWatch.Action.Repath:
                // Wrong shelf: repathing Direct climbs the same cliff. Re-pick Return/aethernet.
                if (!HuntDistances.IsSameFloor(player.Position, currentTargetPosition))
                {
                    log.Debug(
                        "Carrot hunt: still stuck on authored {Id} (wrong floor) — re-routing via camp/aethernet",
                        authoredId);
                    pathfinder.Stop();
                    vnav.Stop();
                    BeginRouteToCurrentAuthored();
                    return true;
                }

                log.Debug(
                    "Carrot hunt: still stuck on authored {Id} after nudge — repathing",
                    authoredId);
                pathfinder.Stop();
                vnav.Stop();
                vnav.PathfindAndMoveCloseTo(currentTargetPosition, false, OpenTreasureCofferChain.PathArrivalRange);
                return true;
            default:
                return false;
        }
    }

    private void TryIssueStuckNudge()
    {
        Vector3 nudge = PathfindingNudge.LateralFrom(player.Position, currentTargetPosition);

        log.Debug(
            "Carrot hunt: stuck approaching authored {Id} — nudging sideways",
            currentAuthored?.Id ?? 0);
        pathfinder.Stop();
        vnav.Stop();
        vnav.PathfindAndMoveCloseTo(nudge, false, 1.5f);
    }

    private void ResetFarStuckWatch() => farStuckWatch.Reset();

    private bool MaybeDismountNear(float distance)
    {
        if (distance > HuntDistances.DismountRadius)
        {
            return false;
        }

        // Still climbing — 2D looks close. Stay mounted.
        if (!HuntDistances.IsSameFloor(player.Position, currentTargetPosition))
        {
            return false;
        }

        // Fortune Carrot use requires being on foot (chests do not).
        if (!DismountAssist.TryDismount(conditions))
        {
            return false;
        }

        if (vnav.IsRunning())
        {
            vnav.Stop();
        }

        return true;
    }

    private bool IsStuckNearTarget(float distance)
    {
        if (distance > HuntDistances.StuckNearRadius)
        {
            ResetApproachProgress();
            return false;
        }

        DateTime now = DateTime.UtcNow;
        if (approachLastProgressUtc == DateTime.MinValue || distance < approachBestDistance - 0.5f)
        {
            approachBestDistance = distance;
            approachLastProgressUtc = now;
            return false;
        }

        if (now - approachLastProgressUtc < HuntDistances.StuckNearTimeout)
        {
            return false;
        }

        log.Debug(
            "Carrot hunt: stuck near target at {Dist:F1}y — trying interact from here",
            distance);
        return true;
    }

    private void ResetApproachProgress()
    {
        approachBestDistance = float.MaxValue;
        approachLastProgressUtc = DateTime.MinValue;
    }

    private void ClearCurrent()
    {
        currentAuthored = null;
        currentLiveCarrotId = null;
        currentTargetPosition = Vector3.Zero;
        ClearWalkVias();
        itemUseIssued = false;
        itemUseIssuedAt = DateTime.MinValue;
        fortuneCarrotCountAtIssue = -1;
        fortuneCarrotUseAttempts = 0;
        waitingForBunnySince = DateTime.MinValue;
        usedLiveCarrotIdsAtPad.Clear();
        ClearEmptyPadCandidate();
        ResetApproachProgress();
        ResetFarStuckWatch();
        ClearHop();
        campReturn.Detach();
        returnThenStop = false;
        returnThenAethernet = false;
    }

    private bool TryUseFortuneCarrot(bool manual = false)
    {
        string throttleKey = manual ? "CarrotHunt::FortuneCarrotManual" : "CarrotHunt::FortuneCarrot";
        int throttleMs = manual ? 500 : 1000;
        return InventoryItemAssist.TryUse(
            FortuneCarrotItemId,
            throttleKey,
            throttleMs,
            log,
            "Carrot hunt");
    }

    private void SoftStopWhileUnconscious()
    {
        SoftStopMovementForPause();
    }

    private void SoftStopMovementForPause()
    {
        chainManager.CancelWhere(name => name.StartsWith("CarrotHunt", StringComparison.Ordinal));
        campReturn.Detach();
        activeTeleportChain = null;
        vnav.Stop();
        pathfinder.Stop();
    }

    private void StopDueToLeavingOccultCrescent()
    {
        log.Information("Left Occult Crescent — stopping carrot hunt");
        Teardown();
        BocchiChat.Print(chat, uiConfig, translator.T(".treasure.carrot_hunt_off_left_zone"));
    }

    private void Teardown()
    {
        if (!Running)
        {
            return;
        }

        Running = false;
        Paused = false;
        Phase = CarrotHuntPhase.Idle;
        finishedAuthoredIds.Clear();
        tour.Clear();
        tourIndex = 0;
        ClearNinjaHideRequirement();
        ClearCurrent();
        stopwatch.Reset();
        vnav.Stop();
        pathfinder.Stop();
        ninjaHide.RestorePreviousGearsetIfNeeded();
        pandoraAutoOpen.Release();
        log.Information("Carrot hunt stopped");
    }
}
