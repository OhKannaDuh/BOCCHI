using BOCCHI.Common;
using BOCCHI.Common.Config;
using BOCCHI.Common.Data.Aethernet;
using BOCCHI.Common.Data.SupportJobs;
using BOCCHI.Common.Data.Zones;
using BOCCHI.Common.Data.Zones.Graph;
using BOCCHI.Common.Services;
using BOCCHI.Common.Targeting;
using BOCCHI.Treasure.ChainRecipes;
using BOCCHI.Treasure.Data;
using BOCCHI.Treasure.Hunt;
using BOCCHI.Treasure.Services;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using ECommons.Throttlers;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.LayoutEngine;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.Interop;
using FFXIVClientStructs.STD;
using Ocelot.Chain;
using Ocelot.Chain.Extensions;
using Ocelot.Config;
using Ocelot.Extensions;
using Ocelot.Ipc.VNavmesh;
using Ocelot.Lifecycle;
using Ocelot.Services.Pathfinding;
using Ocelot.Services.PlayerState;
using Ocelot.Services.Translation;
using Ocelot.Windows;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using TreasureSheet = Lumina.Excel.Sheets.Treasure;
using ObjectKind = Dalamud.Game.ClientState.Objects.Enums.ObjectKind;

namespace BOCCHI.Treasure.Services;

public class TreasureHunterService
(
    TreasureConfig config,
    MovementConfig movementConfig,
    IZoneProvider zones,
    IVNavmeshIpc vnav,
    IPathfinder pathfinder,
    IChainFactory chains,
    IChainManager chainManager,
    IObjectTable objects,
    ICondition conditions,
    IPlayer player,
    IDataManager data,
    IDalamudPluginInterface plugin,
    IPluginLog log,
    IGameGui gui,
    ITreasureTracker tracker,
    ISupportJobFactory supportJobs,
    IClientState client,
    IAutomationModeGuard modeGuard,
    IMp3SoundPlayer sounds,
    NinjaHideAssist ninjaHide,
    IChatGui chat,
    UIConfig uiConfig,
    ITranslator<MainWindow> translator,
    IConfigSaver configSaver,
    PandoraAutoOpenHold pandoraAutoOpen,
    CofferLocationSyncService cofferLocations
) : ITreasureHunter, IOnUpdate, IOnStop
{
    /// <summary>Start open attempts once this close to the coffer (yalms).</summary>
    private const float CofferOpenAttemptRadius = 75f;

    /// <summary>
    ///     After FATE/CE, if the paused resume pad is farther than this, replan from the player
    ///     instead of walking back across the zone.
    /// </summary>
    private const float ResumeNearPlayerMinDistance = 150f;

    /// <summary>How long to wait for WideText after casting Treasure Sight.</summary>
    private static readonly TimeSpan SightCountWait = TimeSpan.FromSeconds(8);

    /// <summary>First stuck recovery: lateral nudge around blocking geometry (#156).</summary>
    private static readonly TimeSpan StuckNudgeTimeout = TimeSpan.FromSeconds(12);

    /// <summary>How long to tolerate no progress toward a coffer before skipping that node.</summary>
    private static readonly TimeSpan StuckNodeTimeout = TimeSpan.FromSeconds(30);

    /// <summary>Skip an unreachable hunt via after this long with no progress.</summary>
    private static readonly TimeSpan StuckViaTimeout = TimeSpan.FromSeconds(10);

    /// <summary>Minimum distance improvement toward the destination that counts as progress.</summary>
    private const float StuckProgressThreshold = 1.5f;

    /// <summary>Below this distance, walk-stuck recovery does not run (open / empty-skip owns it).</summary>
    private const float StuckDetectionMinDistance = OpenTreasureCofferChain.PreferredOpenDistance;

    /// <summary>
    ///     After a stuck nudge, skip an empty pad this close instead of pathing back into the
    ///     same wall (1805 loop: neighbour coffer on radar blocked the normal empty-skip).
    /// </summary>
    private const float StuckEmptySkipRadius = 15f;

    /// <summary>
    ///     Do not re-issue the same vnav dest until this elapses. Covers both the
    ///     pathfind-done / follow-not-started gap and follow dying against geometry.
    /// </summary>
    private static readonly TimeSpan SameDestRepathCooldown = TimeSpan.FromSeconds(2.5);

    private readonly List<TreasureLayoutDatum> layoutTreasure = [];

    /// <summary>Id index over <see cref="layoutTreasure"/>; rebuilt in <see cref="RebuildLayoutIndex"/>.</summary>
    private readonly Dictionary<uint, TreasureLayoutDatum> layoutById = [];

    /// <summary>Treasure objects for the current tick (see <see cref="RefreshTickTreasures"/>).</summary>
    private readonly List<IGameObject> tickTreasures = [];

    private readonly List<HuntPathfinderStep> steps = [];
    private readonly HashSet<uint> checkedNodeIds = [];
    /// <summary>Stuck / geometry skips — never reclaim via Nearby divert (#173).</summary>
    private readonly HashSet<uint> stuckSkippedNodeIds = [];
    private readonly HashSet<uint> lastCompletedRunNodeIds = [];

    private readonly Stopwatch stopwatch = new();
    private Task<ChainResult>? activeChain;

    private IHuntRoutePlanner? pathPlanner;
    private bool planningRoute;
    private bool pendingStartSight;
    private bool waitingForSightCounts;
    private bool sessionStartSightArmed;
    private DateTime sightCastUtc = DateTime.MinValue;
    private int locationsSinceLastSight;

    /// <summary>
    ///     Skip session-start Sight when Illegal Mode (or idle camp) just surveyed — a second
    ///     cast back-to-back usually hits cooldown and adds nothing.
    /// </summary>
    private static readonly TimeSpan FreshSightReuseWindow = TimeSpan.FromSeconds(45);
    private HashSet<uint> excludedNodeIdsForNextRun = [];
    private int? maxLevelOverrideForNextRun;
    private uint? stuckWatchNodeId;
    private float stuckWatchBestDistance = float.MaxValue;
    private DateTime stuckWatchStartedUtc = DateTime.MinValue;
    private bool stuckNudgeIssued;
    private uint? emptyPadCandidateNodeId;
    private DateTime emptyPadCandidateSinceUtc = DateTime.MinValue;

    /// <summary>Force this pad as TSP start on the next plan (Nearby divert / reclaim).</summary>
    private uint? pendingPreferStartNode;

    /// <summary>
    ///     Next plan ignores LastCheckedNodeId so the tour starts at the nearest remaining pad
    ///     (Illegal Mode map hunt after a distant FATE/CE).
    /// </summary>
    private bool pendingRepathNearPlayer;

    /// <summary>South Horn session start: prepend Return before first walk (cleared after first plan).</summary>
    private bool pendingSessionCampReturn;

    /// <summary>South Horn segment rotation: enter the authored route here on the first plan.</summary>
    private uint? pendingEntryNodeId;

    /// <summary>Node → authored segment id (for divert while the planner is null).</summary>
    private readonly Dictionary<uint, string> authoredNodeSegments = [];

    /// <summary>Node → authored order index (peel-off must not jump ahead of the route).</summary>
    private readonly Dictionary<uint, int> authoredNodeOrder = [];

    /// <summary>Hide required until pack threats stay clear (debounced).</summary>
    private bool ninjaHideRequired;

    private readonly NinjaHideRouteGate ninjaHideRouteGate = new();

    /// <summary>Via-points for the current WalkToNode (departure of previous + approach of current).</summary>
    private readonly List<Vector3> walkVias = [];

    private int walkViaIndex;
    private int walkViaStepIndex = -1;
    private int viaStuckIndex = -1;
    private float viaStuckBestDistance = float.MaxValue;
    private DateTime viaStuckStartedUtc = DateTime.MinValue;

    /// <summary>Last snapped target issued to vnav (repath when it drifts).</summary>
    private Vector3? lastNavigateTarget;

    /// <summary>When that target was issued — do not re-queue the same dest until the cooldown elapses.</summary>
    private DateTime lastNavigateIssuedUtc = DateTime.MinValue;

    /// <summary>Skip coffer repath until this time (stuck nudge must be allowed to start).</summary>
    private DateTime holdNavigateUntilUtc = DateTime.MinValue;

    /// <summary>
    ///     Live coffer XZ for the current WalkToNode. Sticky so a one-tick miss in the object
    ///     table cannot flip pathing back to the authored pad and tug-of-war with the chest.
    /// </summary>
    private uint? walkLiveBindNodeId;

    private Vector3? walkLiveBindPosition;

    /// <summary>
    ///     Once we leave the mesh snap to close on the real pad/coffer, do not fall back to the
    ///     snap — that repaths every tick when the live object sits several yalms off-mesh.
    /// </summary>
    private bool navigateClosingOnDestination;

    private Vector3? navigateClosingForDestination;

    public void OnStop() => Teardown();

    public void Update()
    {
        if (!Running)
        {
            return;
        }

        // Zone lock even while paused — leaving OC must fully stop (no resume on return).
        if (!zones.GetZone().IsOccultCrescentZone())
        {
            StopDueToLeavingOccultCrescent();
            return;
        }

        if (Paused)
        {
            if (EzThrottler.Throttle("TreasureHunt::IdlePaused", 5000))
            {
                log.Debug(
                    "Treasure hunt idle: Paused step={Step} vnavRunning={Vnav} mounted={Mounted}",
                    DescribeCurrentStep(),
                    vnav.IsRunning(),
                    conditions[ConditionFlag.Mounted]);
            }

            return;
        }

        if (conditions[ConditionFlag.Unconscious])
        {
            SoftStopMovement();
            if (EzThrottler.Throttle("TreasureHunt::IdleUnconscious", 5000))
            {
                log.Debug("Treasure hunt idle: Unconscious");
            }

            return;
        }

        if (config.SkipUnsafeTreasureWindows && IsUnsafeTreasureWindow())
        {
            if (EzThrottler.Throttle("TreasureHunt::IdleUnsafe", 5000))
            {
                log.Debug("Treasure hunt idle: Unsafe window (SkipUnsafeTreasureWindows)");
            }

            return;
        }

        if (!IsVnavReady)
        {
            if (EzThrottler.Throttle("TreasureHunt::IdleNoVnav", 5000))
            {
                log.Debug("Treasure hunt idle: vnavmesh not ready");
            }

            return;
        }

        RefreshTickTreasures();

        if (activeChain is { IsCompleted: false })
        {
            // Interact often drops Hide; stay stealthed while the open chain runs near threats.
            MaintainNinjaHideDuringInteract();
            if (EzThrottler.Throttle("TreasureHunt::IdleOpenChain", 5000))
            {
                log.Debug("Treasure hunt idle: open chain running step={Step}", DescribeCurrentStep());
            }

            return;
        }

        if (pathPlanner != null)
        {
            if (pathPlanner.State != HuntPathfinderState.FileLoaded)
            {
                if (EzThrottler.Throttle("TreasureHunt::IdlePlanner", 5000))
                {
                    log.Debug("Treasure hunt idle: path planner state={State}", pathPlanner.State);
                }

                return;
            }

            if (!planningRoute)
            {
                if (EzThrottler.Throttle("TreasureHunt::IdlePlannerIdle", 5000))
                {
                    log.Debug("Treasure hunt idle: planner loaded but not planning route");
                }

                return;
            }

            planningRoute = false;
            List<uint> validNodes = GetValidNodesForNextPlan();
            if (pendingPreferStartNode is uint preferLatch
                && !validNodes.Contains(preferLatch)
                && layoutById.TryGetValue(preferLatch, out TreasureLayoutDatum preferLayout))
            {
                List<TreasureData> authored = zones.GetZone().GetTreasureData();
                IReadOnlyList<CrowdsourcedCofferCandidate> liveSpots = cofferLocations.GetAcceptedForCurrentZone();
                if (IsHuntPadAllowed(
                        preferLayout,
                        authored,
                        liveSpots,
                        maxLevelOverrideForNextRun ?? config.HuntMaxLevel,
                        authored.Exists(d => d.Position.HasValue),
                        liveSpots.Count > 0))
                {
                    // Divert latch — pad may have been checked empty while still live.
                    checkedNodeIds.Remove(preferLatch);
                    validNodes.Insert(0, preferLatch);
                }
            }

            steps.Clear();
            List<uint> nearbyPrefix = FindAllLiveNearbyPreferNodes(validNodes);
            if (pendingPreferStartNode is uint latch && validNodes.Contains(latch))
            {
                nearbyPrefix.Remove(latch);
                nearbyPrefix.Insert(0, latch);
            }

            pendingPreferStartNode = null;
            foreach (uint preferId in nearbyPrefix)
            {
                if (!validNodes.Contains(preferId))
                {
                    checkedNodeIds.Remove(preferId);
                    validNodes.Add(preferId);
                }
            }

            RefreshAuthoredSegmentCache(pathPlanner);

            uint? entryNodeId = pendingEntryNodeId;
            pendingEntryNodeId = null;

            // After a distant FATE/CE: ignore authored continue-after + segment peel so we start
            // at the nearest remaining pad to the player instead of walking back across the map.
            bool repathNearPlayer = pendingRepathNearPlayer;
            pendingRepathNearPlayer = false;
            uint? continueAfter = repathNearPlayer ? null : LastCheckedNodeId;

            string? currentSegment = repathNearPlayer
                ? null
                : TryGetCurrentSegment(validNodes, entryNodeId);
            if (currentSegment != null)
            {
                nearbyPrefix = nearbyPrefix
                    .Where(id => authoredNodeSegments.GetValueOrDefault(id) == currentSegment)
                    .ToList();
                nearbyPrefix = FilterNearbyPrefixToAuthoredFrontier(nearbyPrefix, validNodes, currentSegment);
            }

            steps.AddRange(
                pathPlanner
                    .FindPath(player.Position, validNodes, nearbyPrefix, continueAfter, entryNodeId)
                    .GetAwaiter()
                    .GetResult());

            if (pendingSessionCampReturn)
            {
                pendingSessionCampReturn = false;
                bool alreadyInCamp = zones.GetZone().IsInBasecamp();
                if (steps.Count > 0 && steps[0].Type == HuntPathfinderStepType.WalkToNode)
                {
                    // Session start: aethernet (and Return when not already in camp) instead of a
                    // cross-map walk from base to the first pad.
                    uint firstNode = steps[0].NodeId;
                    steps.RemoveAt(0);
                    steps.InsertRange(0, pathPlanner.BuildEntryLeg(firstNode, alreadyInCamp));
                }
                else if (!alreadyInCamp
                         && (steps.Count == 0
                             || steps[0].Type != HuntPathfinderStepType.ReturnToBaseCamp))
                {
                    steps.Insert(0, HuntPathfinderStep.ReturnToBaseCamp());
                }

                log.Debug(
                    "Treasure hunt: prepended session-start camp entry (alreadyInCamp={Already})",
                    alreadyInCamp);
            }

            pathPlanner = null;
            StepIndex = 0;
            // Arm session-start Sight once, after Return/TP is planned — skip if survey is still fresh.
            if (!sessionStartSightArmed)
            {
                bool canSight = config.CastTreasureSightDuringHunt
                                && SupportJobTreasureSight.CanCast(supportJobs);
                bool reuseFreshSurvey = tracker.CountInitialised
                                        && DateTime.UtcNow - tracker.LastCountUpdateUtc < FreshSightReuseWindow;
                pendingStartSight = canSight && !reuseFreshSurvey;
                sessionStartSightArmed = true;
                log.Info(
                    "Treasure hunt: session start Sight {Armed} ({StepCount} step(s))",
                    pendingStartSight ? "armed" : reuseFreshSurvey ? "skipped (fresh survey)" : "skipped",
                    steps.Count);
            }

            if (steps.Count == 0)
            {
                if (TryQueueReturnAfterHunt("empty route after plan"))
                {
                    return;
                }

                log.Warning(
                    "Treasure hunt planned an empty route ({ValidCount} valid node(s) after filters) — ending session",
                    validNodes.Count);
                CompleteHunt();
            }

            return;
        }

        if (TryReprioritizeNearbyLiveCoffer())
        {
            return;
        }

        if (TryFinishSightAndMaybeAbort())
        {
            return;
        }

        if (TryAbortIfTrackerEmpty())
        {
            return;
        }

        if (TryBeginTreasureSight())
        {
            return;
        }

        if (steps.Count == 0 || StepIndex >= steps.Count)
        {
            if (TryQueueReturnAfterHunt("route finished"))
            {
                return;
            }

            CompleteHunt();
            return;
        }

        // Observe completed teleport/return chains before clearing (else the hop restarts).
        if (TryAdvanceCurrentStep())
        {
            HuntPathfinderStep completed = steps[StepIndex];
            if (completed.Type == HuntPathfinderStepType.WalkToNode)
            {
                LastCheckedNodeId = completed.NodeId;
                checkedNodeIds.Add(completed.NodeId);
                locationsSinceLastSight++;
                FinishCurrentPad();

                if (activeChain is { IsCompleted: true })
                {
                    activeChain = null;
                }

                return;
            }

            StepIndex++;
            StepDistance = 0f;
            walkViaStepIndex = -1;
            walkVias.Clear();
            walkViaIndex = 0;
        }

        if (activeChain is { IsCompleted: true })
        {
            activeChain = null;
        }
    }

    /// <summary>Next step is Return/TP (segment exit) — must survive pad completion.</summary>
    private bool NextStepIsTravelHop()
    {
        int next = StepIndex + 1;
        return next < steps.Count
               && steps[next].Type is HuntPathfinderStepType.ReturnToBaseCamp
                   or HuntPathfinderStepType.TeleportToAethernet
                   or HuntPathfinderStepType.WalkToAethernet;
    }

    /// <summary>
    ///     Pad done. Advance onto the following Return / aethernet hop if there is one, else replan.
    /// </summary>
    private void FinishCurrentPad()
    {
        walkViaStepIndex = -1;
        walkVias.Clear();
        walkViaIndex = 0;
        StepDistance = 0f;
        ClearNavigateIssue();
        ClearWalkLiveBind();
        ClearNavigateClosingLatch();

        if (NextStepIsTravelHop())
        {
            StepIndex++;
            return;
        }

        RecalculateRoute();
    }

    public bool Running { get; private set; }

    public bool Paused { get; private set; }

    /// <inheritdoc />
    public bool WaitingForSafeWindow =>
        Running
        && !Paused
        && config.SkipUnsafeTreasureWindows
        && IsUnsafeTreasureWindow();

    public int StepIndex { get; private set; }

    public int StepCount => steps.Count;

    public int CheckedCofferCount => checkedNodeIds.Count;

    public int RemainingCofferCount => steps.Count(s => s.Type == HuntPathfinderStepType.WalkToNode);

    public float StepDistance { get; private set; }

    public TimeSpan Elapsed => stopwatch.Elapsed;

    public uint? LastCheckedNodeId { get; private set; }

    public IReadOnlySet<uint> LastCompletedRunNodeIds => lastCompletedRunNodeIds;

    public bool ManagedByPotsTreasure { get; set; }

    public bool ManagedByIllegalModeFiller { get; set; }

    public bool ManagedByMobFarmer { get; set; }

    public bool IsVnavAvailable => vnav.IsAvailable();

    public bool IsVnavReady => vnav.IsNavmeshReady();

    public void Toggle()
    {
        if (Running)
        {
            Teardown();
            return;
        }

        modeGuard.EnsureExclusive(AutomationMode.TreasureHunt);
        ManagedByPotsTreasure = false;
        ManagedByIllegalModeFiller = false;
        ManagedByMobFarmer = false;
        BeginHuntSession();
    }

    public void StartManaged()
    {
        if (Running)
        {
            return;
        }

        BeginHuntSession();
    }

    public void ConfigureManagedRun(IReadOnlySet<uint> excludedNodeIds, int? maxLevelOverride = null)
    {
        ManagedByPotsTreasure = true;
        excludedNodeIdsForNextRun = excludedNodeIds.ToHashSet();
        maxLevelOverrideForNextRun = maxLevelOverride;
    }

    public bool RecalculateRoute()
    {
        if (!Running || Paused || !IsVnavReady)
        {
            return false;
        }

        // Also called from Pots & Treasure outside our own Update tick.
        RefreshTickTreasures();

        TreasureHuntPathfinder? planner = CreatePathPlanner();
        if (planner == null || planner.State != HuntPathfinderState.FileLoaded)
        {
            log.Warning("Failed to initialize treasure hunt path data for route recalculation");
            return false;
        }

        SoftStopMovement();
        steps.Clear();
        StepIndex = 0;
        StepDistance = 0f;
        walkViaStepIndex = -1;
        walkViaIndex = 0;
        walkVias.Clear();
        ResetViaStuckWatch();
        pendingStartSight = false;
        waitingForSightCounts = false;
        sightCastUtc = DateTime.MinValue;
        ClearEmptyPadCandidate();
        // Preserve Every-N Sight counter across replans.
        pathPlanner = planner;
        planningRoute = true;

        log.Debug(
            "Treasure hunt route recalculation requested; {CheckedCount} checked nodes excluded",
            checkedNodeIds.Count);
        return true;
    }

    private void BeginHuntSession()
    {
        stopwatch.Restart();
        StepIndex = 0;
        LastCheckedNodeId = null;
        Paused = false;
        steps.Clear();
        layoutTreasure.Clear();
        layoutById.Clear();
        pendingStartSight = false;
        waitingForSightCounts = false;
        sessionStartSightArmed = false;
        sightCastUtc = DateTime.MinValue;
        locationsSinceLastSight = 0;
        ClearNinjaHideRequirement();
        pendingPreferStartNode = null;
        pendingRepathNearPlayer = false;
        pendingSessionCampReturn = false;
        pendingEntryNodeId = null;
        authoredNodeSegments.Clear();
        authoredNodeOrder.Clear();
        checkedNodeIds.Clear();
        stuckSkippedNodeIds.Clear();
        ClearEmptyPadCandidate();
        ClearWalkLiveBind();
        ClearNavigateClosingLatch();
        ResetStuckWatch();
        if (!ManagedByPotsTreasure)
        {
            lastCompletedRunNodeIds.Clear();
            excludedNodeIdsForNextRun.Clear();
            maxLevelOverrideForNextRun = null;
        }

        pathPlanner = CreatePathPlanner();
        if (pathPlanner == null || pathPlanner.State != HuntPathfinderState.FileLoaded)
        {
            log.Error("Failed to initialize treasure hunt path data");
            Teardown();
            return;
        }

        IZone zone = zones.GetZone();
        if (zone.ZoneId == ZoneId.SouthHorn)
        {
            string? startSegment = NextRotationSegment(
                pathPlanner.SegmentIds,
                config.LastSouthHornStartSegment);
            if (startSegment != null)
            {
                config.LastSouthHornStartSegment = startSegment;
                configSaver.Save();
                pendingEntryNodeId = pathPlanner.TryGetSegmentFirstNode(startSegment);
                // Always plan camp→aethernet entry for the first pad (Return only when not already
                // in camp). Skipping this while in camp caused a ~1000y walk across the map.
                pendingSessionCampReturn = true;
            }

            log.Debug(
                "Treasure hunt South Horn: start segment {Segment} (pad {Pad}); camp entry pending (inCamp={InCamp})",
                startSegment ?? "-",
                pendingEntryNodeId?.ToString() ?? "-",
                zone.IsInBasecamp());
        }

        Running = true;
        planningRoute = true;
        pandoraAutoOpen.Hold();
    }

    /// <summary>Rotate South Horn start segment; unknown id → first segment.</summary>
    private static string? NextRotationSegment(IReadOnlyList<string> segmentIds, string? lastStartSegment)
    {
        if (segmentIds.Count == 0)
        {
            return null;
        }

        int last = lastStartSegment == null
            ? -1
            : IndexOfSegment(segmentIds, lastStartSegment);

        return last < 0 ? segmentIds[0] : segmentIds[(last + 1) % segmentIds.Count];
    }

    private static int IndexOfSegment(IReadOnlyList<string> segmentIds, string segmentId)
    {
        for (int i = 0; i < segmentIds.Count; i++)
        {
            if (string.Equals(segmentIds[i], segmentId, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    public void Pause()
    {
        if (!Running || Paused)
        {
            return;
        }

        Paused = true;
        SoftStopMovement();
        stopwatch.Stop();
        log.Debug(
            "Treasure hunt Pause step={Step} vnavRunning={Vnav}",
            DescribeCurrentStep(),
            vnav.IsRunning());
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

        log.Debug("Treasure hunt Resume step={Step}", DescribeCurrentStep());
    }

    public void ResumeNearPlayer()
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

        log.Debug("Treasure hunt ResumeNearPlayer step={Step}", DescribeCurrentStep());

        if (!TryGetResumeCoffer(out uint resumeId, out Vector3 resumePos))
        {
            return;
        }

        float dist = player.Position.Distance2D(resumePos);
        if (dist <= ResumeNearPlayerMinDistance)
        {
            return;
        }

        pendingRepathNearPlayer = true;
        if (!RecalculateRoute())
        {
            pendingRepathNearPlayer = false;
            return;
        }

        log.Info(
            "Treasure hunt: resume pad {NodeId} is {Dist:F0}y away — repathing from player",
            resumeId,
            dist);
    }

    public HuntPathfinderStep? GetCurrentStep()
    {
        if (StepIndex < 0 || StepIndex >= steps.Count)
        {
            return null;
        }

        return steps[StepIndex];
    }

    private string DescribeCurrentStep()
    {
        HuntPathfinderStep? step = GetCurrentStep();
        if (step is null)
        {
            return planningRoute ? "planning" : $"none (idx={StepIndex}/{steps.Count})";
        }

        return $"{step.Type}/node={step.NodeId}";
    }

    public bool TryGetResumeCoffer(out uint nodeId, out Vector3 position)
    {
        nodeId = 0;
        position = default;
        if (!Running || steps.Count == 0)
        {
            return false;
        }

        for (int i = Math.Max(0, StepIndex); i < steps.Count; i++)
        {
            HuntPathfinderStep step = steps[i];
            if (step.Type != HuntPathfinderStepType.WalkToNode)
            {
                continue;
            }

            if (!TryGetLayout(step.NodeId, out TreasureLayoutDatum layout))
            {
                continue;
            }

            nodeId = step.NodeId;
            position = layout.Position;
            return true;
        }

        return false;
    }

    public unsafe bool FlagResumePoint()
    {
        if (!TryGetResumeCoffer(out uint nodeId, out Vector3 position))
        {
            return false;
        }

        AgentMap* map = AgentMap.Instance();
        if (map == null)
        {
            return false;
        }

        map->SetFlagMapMarker(client.TerritoryType, client.MapId, position);
        log.Debug("Flagged treasure hunt resume coffer {NodeId} at {Position:f0}", nodeId, position);
        return true;
    }

    /// <summary>Stop movement/chains without clearing the planned route.</summary>
    private void SoftStopMovement()
    {
        chainManager.CancelWhere(name => name.StartsWith("TreasureHunt", StringComparison.Ordinal));
        pathfinder.Stop();
        vnav.Stop();
        activeChain = null;
        ClearNavigateIssue();
        ClearNavigateClosingLatch();
        ResetStuckWatch();
    }

    private bool TryRecoverFromStuckWalk(HuntPathfinderStep step, float distance)
    {
        if (distance <= StuckDetectionMinDistance)
        {
            ResetStuckWatch();
            return false;
        }

        // Pathfind has not started moving yet — don't count compute time as a stuck walk.
        if (vnav.IsPathfinding())
        {
            return false;
        }

        DateTime now = DateTime.UtcNow;
        if (stuckWatchNodeId != step.NodeId)
        {
            StartStuckWatch(step.NodeId, distance, now);
            return false;
        }

        // Progress is distance to the goal, not absolute movement. Restart the clock with it —
        // otherwise the timeout measures "time since the walk began" and any pad further away than
        // StuckNodeTimeout of travel gets skipped while still closing on it.
        if (distance < stuckWatchBestDistance - StuckProgressThreshold)
        {
            stuckWatchBestDistance = distance;
            stuckWatchStartedUtc = now;
            stuckNudgeIssued = false;
            return false;
        }

        if (!stuckNudgeIssued && now - stuckWatchStartedUtc >= StuckNudgeTimeout)
        {
            stuckNudgeIssued = true;
            TryIssueStuckNudge(step);
            return true;
        }

        if (now - stuckWatchStartedUtc < StuckNodeTimeout)
        {
            return false;
        }

        log.Warning(
            "Treasure hunt appears stuck reaching coffer {NodeId}; excluding it and recalculating the route",
            step.NodeId);
        checkedNodeIds.Add(step.NodeId);
        stuckSkippedNodeIds.Add(step.NodeId);
        LastCheckedNodeId = step.NodeId;
        ResetStuckWatch();
        FinishCurrentPad();
        return true;
    }

    private void TryIssueStuckNudge(HuntPathfinderStep step)
    {
        Vector3 dest = walkLiveBindNodeId == step.NodeId && walkLiveBindPosition is { } live
            ? live
            : TryGetLayout(step.NodeId, out TreasureLayoutDatum layout)
                ? layout.Position
                : player.Position;

        if (TreasurePathing.TrySnapToNavmesh(dest, player.Position.Y, vnav, out Vector3 pathableDest))
        {
            dest = pathableDest;
        }

        Vector3 nudge = PathfindingNudge.LateralFrom(player.Position, dest);
        if (TreasurePathing.TrySnapToNavmesh(nudge, player.Position.Y, vnav, out Vector3 pathableNudge))
        {
            nudge = pathableNudge;
        }

        log.Debug("Treasure hunt stuck near {NodeId} — nudging sideways around geometry (#156)", step.NodeId);
        pathfinder.Stop();
        vnav.Stop();
        ClearNavigateClosingLatch();
        lastNavigateTarget = nudge;
        lastNavigateIssuedUtc = DateTime.UtcNow;
        // TryNavigateToward would overwrite this on the next tick if we don't hold.
        holdNavigateUntilUtc = DateTime.UtcNow + TimeSpan.FromSeconds(3);
        vnav.PathfindAndMoveCloseTo(nudge, false, 1.5f);
    }

    /// <summary>
    ///     Stuck on a pad with no live coffer — skip it. Neighbour chests on radar used to
    ///     pin us here: empty-skip required a streamed neighbour near <em>this</em> pad,
    ///     divert required one in layout range, and neither fired so we re-queued forever.
    /// </summary>
    private bool TrySkipEmptyAfterStuckNudge(HuntPathfinderStep step, Vector3 layoutDestination, float dist2d)
    {
        if (!stuckNudgeIssued || !IsSameFloor(layoutDestination) || dist2d > StuckEmptySkipRadius)
        {
            return false;
        }

        log.Debug(
            "Treasure hunt: still no live coffer at {NodeId} after stuck nudge ({Dist:F0}y) — skipping",
            step.NodeId,
            dist2d);
        checkedNodeIds.Add(step.NodeId);
        stuckSkippedNodeIds.Add(step.NodeId);
        LastCheckedNodeId = step.NodeId;
        ClearEmptyPadCandidate();
        ResetStuckWatch();
        FinishCurrentPad();
        return true;
    }

    private void StartStuckWatch(uint nodeId, float distance, DateTime now)
    {
        stuckWatchNodeId = nodeId;
        stuckWatchBestDistance = distance;
        stuckWatchStartedUtc = now;
        stuckNudgeIssued = false;
    }

    private void ResetStuckWatch()
    {
        stuckWatchNodeId = null;
        stuckWatchBestDistance = float.MaxValue;
        stuckWatchStartedUtc = DateTime.MinValue;
        stuckNudgeIssued = false;
    }

    private void ResetViaStuckWatch()
    {
        viaStuckIndex = -1;
        viaStuckBestDistance = float.MaxValue;
        viaStuckStartedUtc = DateTime.MinValue;
    }

    /// <summary>Skip the current via when vnav cannot make progress (off-mesh / blocked).</summary>
    private bool TrySkipStuckVia(uint nodeId, float distance)
    {
        DateTime now = DateTime.UtcNow;
        if (viaStuckIndex != walkViaIndex)
        {
            viaStuckIndex = walkViaIndex;
            viaStuckBestDistance = distance;
            viaStuckStartedUtc = now;
            return false;
        }

        if (distance < viaStuckBestDistance - StuckProgressThreshold)
        {
            viaStuckBestDistance = distance;
            viaStuckStartedUtc = now;
            return false;
        }

        if (now - viaStuckStartedUtc < StuckViaTimeout)
        {
            return false;
        }

        log.Warning(
            "Treasure hunt: skipping stuck via {ViaIndex} toward node {NodeId} (dist {Dist:F1})",
            walkViaIndex,
            nodeId,
            distance);
        walkViaIndex++;
        ResetViaStuckWatch();
        vnav.Stop();
        pathfinder.Stop();
        return true;
    }

    /// <summary>
    /// All remaining layout pads for live Nearby coffers, closest first (exclusive pad match).
    /// </summary>
    private List<uint> FindAllLiveNearbyPreferNodes(IReadOnlyList<uint> validNodes)
    {
        if (validNodes.Count == 0)
        {
            return [];
        }

        HashSet<uint> valid = validNodes.ToHashSet();
        List<(TreasureCoffer Coffer, float Dist)> lives = [];
        foreach (TreasureCoffer coffer in tracker.Treasures)
        {
            if (!coffer.IsValid() || !MatchesLiveHuntFilter(coffer))
            {
                continue;
            }

            float distToPlayer = player.Position.Distance2D(coffer.GetPosition());
            if (distToPlayer > HuntDistances.NearbyLiveDivertRange)
            {
                continue;
            }

            lives.Add((coffer, distToPlayer));
        }

        if (lives.Count == 0)
        {
            return [];
        }

        lives.Sort((a, b) => a.Dist.CompareTo(b.Dist));
        HashSet<uint> claimedPads = [];
        List<uint> result = [];
        foreach ((TreasureCoffer coffer, float distToPlayer) in lives)
        {
            uint? nodeId = FindNearestUnclaimedLayoutNode(
                coffer.GetPosition(),
                valid,
                claimedPads,
                HuntDistances.LayoutProximityRadiusSq);
            if (nodeId is not uint id)
            {
                continue;
            }

            if (TreasureHuntPathOverrides.IsUnreachable(zones.GetZone().ZoneId, id))
            {
                continue;
            }

            claimedPads.Add(id);
            result.Add(id);
        }

        return result;
    }

    private bool MatchesLiveHuntFilter(TreasureCoffer coffer)
    {
        CofferType type = coffer.GetCofferType();
        if (type is not (CofferType.Bronze or CofferType.Silver))
        {
            return false;
        }

        return !config.HuntSilverChestsOnly || type == CofferType.Silver;
    }

    /// <summary>True when an unopened bronze/silver hunt coffer is still near the player.</summary>
    private bool HasUnopenedLiveHuntCofferNearPlayer(float range)
    {
        foreach (TreasureCoffer coffer in tracker.Treasures)
        {
            if (!coffer.IsValid() || !MatchesLiveHuntFilter(coffer))
            {
                continue;
            }

            if (player.Position.Distance2D(coffer.GetPosition()) <= range)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Nearest unclaimed pad; peel uses LayoutProximityRadius, not MatchRadius.</summary>
    private uint? FindNearestUnclaimedLayoutNode(
        Vector3 livePosition,
        HashSet<uint> validNodes,
        HashSet<uint> claimedPads,
        float maxDistSq)
    {
        uint? bestId = null;
        float bestDistSq = maxDistSq;
        foreach (uint nodeId in validNodes)
        {
            if (claimedPads.Contains(nodeId))
            {
                continue;
            }

            if (!TryGetLayout(nodeId, out TreasureLayoutDatum layout))
            {
                continue;
            }

            float dist2d = layout.Position.Distance2D(livePosition);
            float dist2dSq = dist2d * dist2d;
            if (dist2dSq > bestDistSq)
            {
                continue;
            }

            bestDistSq = dist2dSq;
            bestId = nodeId;
        }

        return bestId;
    }

    /// <summary>Divert mid-route when a nearer live coffer remains (including Return / aethernet).</summary>
    private bool TryReprioritizeNearbyLiveCoffer()
    {
        if (planningRoute || pathPlanner != null || activeChain != null)
        {
            return false;
        }

        HuntPathfinderStep? current = GetCurrentStep();
        if (current == null)
        {
            return false;
        }

        bool travelHop = current.Type is HuntPathfinderStepType.ReturnToBaseCamp
            or HuntPathfinderStepType.TeleportToAethernet
            or HuntPathfinderStepType.WalkToAethernet;

        if (current.Type is not HuntPathfinderStepType.WalkToNode && !travelHop)
        {
            return false;
        }

        float currentDist;
        if (current.Type == HuntPathfinderStepType.WalkToNode)
        {
            currentDist = GetWalkNodeGoalDistance(current.NodeId);
        }
        else
        {
            currentDist = float.MaxValue;
        }

        // Already at/near the pad — don't U-turn to another coffer mid-open.
        if (current.Type == HuntPathfinderStepType.WalkToNode
            && currentDist <= HuntDistances.NearbyLiveDivertMinCurrentDistance)
        {
            return false;
        }

        // Throttle before the pad scan — divert at most every 1.5s.
        if (!EzThrottler.Throttle("TreasureHuntReprioritize", 1500))
        {
            return false;
        }

        HashSet<uint> candidates = GetDivertCandidateNodes(current);
        List<uint> remaining = GetValidNodesForNextPlan();
        if (TryGetCurrentSegment(remaining, null) is string segment)
        {
            // Divert only within the current segment; no segment → exclude.
            candidates.RemoveWhere(id =>
                authoredNodeSegments.GetValueOrDefault(id) != segment);

            // Stay on authored order, but reclaim a pad that was empty-skipped while the
            // coffer was still loading — GetDivertCandidateNodes already re-adds those.
            if (TryGetAuthoredFrontier(remaining, segment) is uint frontier)
            {
                candidates.RemoveWhere(id =>
                    id != frontier
                    && !(checkedNodeIds.Contains(id)
                         && !stuckSkippedNodeIds.Contains(id)
                         && IsWalkGoalLiveNearby(id)));
            }
            else
            {
                candidates.Clear();
            }
        }

        List<uint> nearby = FindAllLiveNearbyPreferNodes(candidates.ToList());
        if (nearby.Count == 0)
        {
            return false;
        }

        uint nearbyId = nearby[0];
        if (!TryGetLayout(nearbyId, out TreasureLayoutDatum layout))
        {
            return false;
        }

        IGameObject? present = FindUnopenedTreasureNear(layout.Position, HuntDistances.MatchRadius)
                               ?? FindTreasureForLayout(layout.Position, nearbyId);
        if (present == null || OpenTreasureCofferChain.IsOpenedOrLooted(present))
        {
            return false;
        }

        float nearbyDist = player.Position.Distance2D(present.Position);

        bool currentIsLiveNearby = current.Type == HuntPathfinderStepType.WalkToNode
                                   && IsWalkGoalLiveNearby(current.NodeId);

        if (currentIsLiveNearby)
        {
            // Already walking to a live Nearby — only peel if another is clearly closer.
            if (nearbyDist + 5f >= currentDist)
            {
                return false;
            }
        }
        else if (current.Type == HuntPathfinderStepType.WalkToNode
                 && currentDist <= HuntDistances.EmptyPadSkipRadius)
        {
            // Near pad: wait for stream/empty-skip; don't peel (false empty U-turns).
            return false;
        }
        else if (nearbyDist + HuntDistances.NearbyLiveDivertClearAdvantage >= currentDist
                 && currentDist <= HuntDistances.NearbyLiveDivertRange)
        {
            // Current goal is also "near" but empty/wrong pad — still require a clear win.
            return false;
        }

        // Stuck / unpathable pads must stay skipped — reclaiming them loops the wind jump (#173).
        if (stuckSkippedNodeIds.Contains(nearbyId))
        {
            return false;
        }

        // False empty-skip may have checked this pad while the coffer is still live.
        checkedNodeIds.Remove(nearbyId);
        pendingPreferStartNode = nearbyId;

        log.Debug(
            "Treasure hunt diverting to live coffer {NearbyId} at {NearbyDist:F1}y (was {CurrentType} {CurrentId} at {CurrentDist:F1}y)",
            nearbyId,
            nearbyDist,
            current.Type,
            current.NodeId,
            currentDist > 1e6f ? -1f : currentDist);

        if (RecalculateRoute())
        {
            return true;
        }

        pendingPreferStartNode = null;
        return false;
    }

    /// <summary>True when the walk goal still has an unopened live coffer within divert range of the player.</summary>
    private bool IsWalkGoalLiveNearby(uint nodeId)
    {
        if (!TryGetLayout(nodeId, out TreasureLayoutDatum layout))
        {
            return false;
        }

        IGameObject? present = FindTreasureForLayout(layout.Position, nodeId);
        if (present == null || OpenTreasureCofferChain.IsOpenedOrLooted(present))
        {
            return false;
        }

        return player.Position.Distance2D(present.Position) <= HuntDistances.NearbyLiveDivertRange;
    }

    /// <summary>
    /// Remaining route pads plus other matching layout pads (including already-checked),
    /// so a live Nearby coffer can map to its real pad after a false empty skip.
    /// </summary>
    private HashSet<uint> GetDivertCandidateNodes(HuntPathfinderStep current)
    {
        HashSet<uint> ids = GetValidNodesForNextPlan().ToHashSet();
        if (current.Type == HuntPathfinderStepType.WalkToNode)
        {
            ids.Remove(current.NodeId);
        }

        int maxLevel = maxLevelOverrideForNextRun ?? config.HuntMaxLevel;
        List<TreasureData> authored = zones.GetZone().GetTreasureData();
        IReadOnlyList<CrowdsourcedCofferCandidate> liveSpots = cofferLocations.GetAcceptedForCurrentZone();
        bool hasAuthoredPositions = authored.Exists(d => d.Position.HasValue);
        bool hasCrowdsourced = liveSpots.Count > 0;
        foreach (TreasureLayoutDatum layout in layoutTreasure)
        {
            if (ids.Contains(layout.Id))
            {
                continue;
            }

            if (!MatchesHuntCofferFilter(layout.ModelId)
                || IsLayoutCofferOpened(layout.Id)
                || stuckSkippedNodeIds.Contains(layout.Id))
            {
                continue;
            }

            if ((hasAuthoredPositions || hasCrowdsourced)
                && !IsHuntPadAllowed(
                    layout,
                    authored,
                    liveSpots,
                    maxLevel,
                    hasAuthoredPositions,
                    hasCrowdsourced))
            {
                continue;
            }

            ids.Add(layout.Id);
        }

        return ids;
    }

    private float GetWalkNodeGoalDistance(uint nodeId)
    {
        if (!TryGetLayout(nodeId, out TreasureLayoutDatum layout))
        {
            return StepDistance;
        }

        IGameObject? present = FindTreasureForLayout(layout.Position, nodeId);
        Vector3 destination = present?.Position ?? layout.Position;
        return player.Position.Distance2D(destination);
    }

    private bool TryBeginTreasureSight()
    {
        if (!config.CastTreasureSightDuringHunt || !SupportJobTreasureSight.CanCast(supportJobs))
        {
            pendingStartSight = false;
            return false;
        }

        if (waitingForSightCounts || activeChain != null)
        {
            return false;
        }

        // Don't interrupt return / teleport mid-step.
        HuntPathfinderStep? step = GetCurrentStep();
        if (step is { Type: HuntPathfinderStepType.ReturnToBaseCamp or HuntPathfinderStepType.TeleportToAethernet })
        {
            return false;
        }

        bool dueForStart = pendingStartSight;
        bool dueForRefresh = !pendingStartSight
                             && steps.Count > 0
                             && StepIndex < steps.Count
                             && locationsSinceLastSight >= config.TreasureSightEveryNLocations;

        if (!dueForStart && !dueForRefresh)
        {
            return false;
        }

        // Defer while fighting or job-swap gates are closed — starting the chain early
        // only burns a 15s WaitUntil step (Dismount / ToFreelancer / RestoreJob).
        if (conditions[ConditionFlag.InCombat] || PhantomJobChangeGate.IsBlocked(conditions))
        {
            return false;
        }

        // Sight is due: stop pathing and get on foot before starting the chain.
        // Returning false here used to fall through to MaybeMount — dismount + remount every tick.
        if (DismountAssist.TryDismount(conditions))
        {
            SoftStopMovement();
            return true;
        }

        SoftStopMovement();
        pendingStartSight = false;
        waitingForSightCounts = true;
        sightCastUtc = DateTime.UtcNow;
        locationsSinceLastSight = 0;

        log.Debug(
            "Treasure hunt: casting Treasure Sight ({Reason})",
            dueForStart ? "session start" : $"every {config.TreasureSightEveryNLocations} locations");

        activeChain = chainManager.Manage(
            chains.Create("TreasureHunt::TreasureSight")
                .Then<HuntTreasureSightChain>()
        );

        return true;
    }

    /// <returns>True when the caller should skip the rest of this tick.</returns>
    private bool TryFinishSightAndMaybeAbort()
    {
        if (!waitingForSightCounts)
        {
            return false;
        }

        if (activeChain is { IsCompleted: false })
        {
            return true;
        }

        if (activeChain is { IsCompleted: true })
        {
            bool castOk = activeChain.IsCompletedSuccessfully && (activeChain.Result?.IsSuccess ?? false);
            activeChain = null;
            if (!castOk)
            {
                waitingForSightCounts = false;
                log.Warning("Treasure Sight cast during hunt failed; continuing route");
                return false;
            }
        }

        bool refreshed = tracker.LastCountUpdateUtc >= sightCastUtc;
        bool timedOut = DateTime.UtcNow - sightCastUtc >= SightCountWait;
        if (!refreshed && !timedOut)
        {
            return true;
        }

        waitingForSightCounts = false;

        if (ShouldAbortForNoChests())
        {
            FinishHuntEarly("Treasure Sight reports no remaining coffers");
            return true;
        }

        int trimmed = TrimNearbyEmptyNodesAfterSight();
        log.Debug(
            "Treasure Sight refresh: {Bronze} bronze / {Silver} silver remaining; trimmed {Trimmed} nearby empty pad(s)",
            tracker.BronzeChests,
            tracker.SilverChests,
            trimmed);

        // Sight only changes the route when it actually trimmed pads.
        if (trimmed > 0)
        {
            RecalculateRoute();
        }

        return true;
    }

    /// <summary>
    /// After Sight, drop remaining layout nodes already empty and within walk-up range
    /// so we do not detour onto pads the object table already proves vacant.
    /// Distant empties are still walked to, then skipped by the normal empty-pad check.
    /// </summary>
    private int TrimNearbyEmptyNodesAfterSight()
    {
        int trimmed = 0;
        foreach (uint nodeId in GetValidNodesForNextPlan().ToList())
        {
            if (!TryGetLayout(nodeId, out TreasureLayoutDatum spot))
            {
                continue;
            }

            // Trim only nearby same-floor empties after Sight.
            if (FindTreasureForLayout(spot.Position, nodeId) != null
                || player.Position.Distance2D(spot.Position) > config.EmptyPadTrustDistance
                || !IsSameFloor(spot.Position))
            {
                continue;
            }

            checkedNodeIds.Add(nodeId);
            trimmed++;
        }

        return trimmed;
    }

    private bool TrackerReportsNoWantedChests()
    {
        if (!tracker.CountInitialised)
        {
            return false;
        }

        if (config.HuntSilverChestsOnly)
        {
            return tracker.SilverChests <= 0;
        }

        return tracker.BronzeChests + tracker.SilverChests <= 0;
    }

    private bool HasRemainingCofferSteps()
    {
        for (int i = StepIndex; i < steps.Count; i++)
        {
            if (steps[i].Type != HuntPathfinderStepType.ReturnToBaseCamp)
            {
                return true;
            }
        }

        return false;
    }

    private bool ShouldAbortForNoChests() =>
        TrackerReportsNoWantedChests() && HasRemainingCofferSteps();

    /// <summary>
    ///     Sight counts (and opens that decrement them) can hit 0 while the authored route still
    ///     has empty pads — stop instead of walking the rest of the map.
    /// </summary>
    private bool TryAbortIfTrackerEmpty()
    {
        if (waitingForSightCounts || pendingStartSight)
        {
            return false;
        }

        if (!ShouldAbortForNoChests())
        {
            return false;
        }

        FinishHuntEarly("no remaining coffers");
        return true;
    }

    private void FinishHuntEarly(string reason)
    {
        log.Info($"Treasure hunt ending early: {reason}");
        SoftStopMovement();
        waitingForSightCounts = false;
        pendingStartSight = false;

        if (StepIndex < steps.Count)
        {
            steps.RemoveRange(StepIndex, steps.Count - StepIndex);
        }
    }

    private bool TryAdvanceCurrentStep()
    {
        HuntPathfinderStep step = steps[StepIndex];
        return step.Type switch
        {
            HuntPathfinderStepType.WalkToNode => HandleWalkToNode(step),
            HuntPathfinderStepType.ReturnToBaseCamp => HandleReturnToBaseCamp(),
            HuntPathfinderStepType.WalkToAethernet => HandleWalkToAethernet(step),
            HuntPathfinderStepType.TeleportToAethernet => HandleTeleportToAethernet(step),
            var _ => true
        };
    }

    private bool HandleWalkToNode(HuntPathfinderStep step)
    {
        if (!Running)
        {
            vnav.Stop();
            return true;
        }

        if (!TryGetLayout(step.NodeId, out TreasureLayoutDatum layout))
        {
            log.Warning(
                "Treasure hunt: node {NodeId} is no longer in the layout — skipping and recalculating",
                step.NodeId);
            checkedNodeIds.Add(step.NodeId);
            LastCheckedNodeId = step.NodeId;
            ResetStuckWatch();
            FinishCurrentPad();
            return false;
        }

        Vector3 layoutDestination = layout.Position;

        if (walkLiveBindNodeId != step.NodeId)
        {
            ClearWalkLiveBind();
            walkLiveBindNodeId = step.NodeId;
        }

        // Opened/looted (incl. VBM) — skip before vias.
        if (TryCompleteOpenedLayoutCoffer(layoutDestination, step.NodeId))
        {
            return true;
        }

        EnsureWalkVias(step);
        if (walkViaIndex < walkVias.Count)
        {
            Vector3 via = walkVias[walkViaIndex];
            float viaDist = player.Position.Distance2D(via);
            StepDistance = viaDist;

            const float viaArrival = 2.5f;
            if (viaDist > viaArrival)
            {
                if (TrySkipStuckVia(step.NodeId, viaDist))
                {
                    return false;
                }

                TryNavigateToward(
                    via,
                    viaArrival,
                    OpenTreasureCofferChain.PathArrivalRange);
                return false;
            }

            walkViaIndex++;
            ResetViaStuckWatch();
            vnav.Stop();
            return false;
        }

        if (activeChain != null)
        {
            if (!activeChain.IsCompleted)
            {
                return false;
            }

            bool openOk = activeChain.IsCompletedSuccessfully
                          && (activeChain.Result?.IsSuccess ?? false);
            activeChain = null;

            IGameObject? afterOpen = FindTreasureForLayout(layoutDestination, step.NodeId);
            if (openOk
                || (afterOpen != null && OpenTreasureCofferChain.IsOpenedOrLooted(afterOpen)))
            {
                vnav.Stop();
                ResetStuckWatch();
                return true;
            }

            log.Warning(
                "Treasure hunt: could not open coffer {NodeId} — skipping and recalculating",
                step.NodeId);
            checkedNodeIds.Add(step.NodeId);
            // Divert reclaims false empty-skips from checkedNodeIds; keep open failures
            // sticky like stuck pads so we do not loop on the same live coffer (SH 1821).
            stuckSkippedNodeIds.Add(step.NodeId);
            LastCheckedNodeId = step.NodeId;
            ResetStuckWatch();
            FinishCurrentPad();
            return false;
        }

        IGameObject? present = FindTreasureForLayout(layoutDestination, step.NodeId);
        if (present != null && !OpenTreasureCofferChain.IsOpenedOrLooted(present))
        {
            walkLiveBindPosition = present.Position;
        }

        Vector3 destination = walkLiveBindPosition ?? present?.Position ?? layoutDestination;

        float dist2d = player.Position.Distance2D(destination);
        StepDistance = dist2d;

        if (present == null && walkLiveBindPosition == null)
        {
            // Outside the trust slider — keep walking. Inside it (or when a neighbour coffer
            // proves the region streamed), empty-skip may run (#168).
            bool stillApproaching = IsStillApproachingOutsideTrust(dist2d)
                                    && !RegionProvesStream(layoutDestination);
            if (stillApproaching)
            {
                ClearEmptyPadCandidate();
            }

            // Empty-skip first: a live coffer elsewhere on radar must not pin us to an empty pad (#168).
            if (!stillApproaching
                && CanTrustEmptyPad(layoutDestination)
                && ConfirmEmptyPad(step.NodeId))
            {
                log.Debug(
                    "Treasure hunt: no live coffer at layout {NodeId} at {Dist:F0}y — skipping "
                    + "({Nearby} coffer(s) streamed within {Radius:F0}y, {Total} in object table)",
                    step.NodeId,
                    dist2d,
                    CountTreasuresNear(layoutDestination, HuntDistances.EmptyPadEarlySkipRadius),
                    HuntDistances.EmptyPadEarlySkipRadius,
                    tickTreasures.Count);
                checkedNodeIds.Add(step.NodeId);
                LastCheckedNodeId = step.NodeId;
                ClearEmptyPadCandidate();
                ResetStuckWatch();
                FinishCurrentPad();
                return false;
            }

            if (TrySkipEmptyAfterStuckNudge(step, layoutDestination, dist2d))
            {
                return false;
            }

            // Still see a hunt coffer on radar — peel to it when it matches this layout area.
            if (HasUnopenedLiveHuntCofferNearPlayer(HuntDistances.NearbyLiveDivertRange))
            {
                IGameObject? loose = FindUnopenedTreasureNear(
                    layoutDestination,
                    HuntDistances.LayoutProximityRadius);
                if (loose != null)
                {
                    present = loose;
                    walkLiveBindPosition = loose.Position;
                    destination = loose.Position;
                    dist2d = player.Position.Distance2D(destination);
                    StepDistance = dist2d;
                }
                else
                {
                    TryNavigateToward(
                        destination,
                        OpenTreasureCofferChain.PreferredOpenDistance,
                        OpenTreasureCofferChain.PathArrivalRange);
                    TryRecoverFromStuckWalk(step, StepDistance);
                    return false;
                }
            }

            if (present == null)
            {
                TryNavigateToward(
                    destination,
                    OpenTreasureCofferChain.PreferredOpenDistance,
                    OpenTreasureCofferChain.PathArrivalRange);
                TryRecoverFromStuckWalk(step, StepDistance);
                return false;
            }
        }
        else if (present == null)
        {
            // Bound a live coffer earlier this pad — keep walking to it through brief radar gaps.
            // After a nudge with still no radar, the bind is stale: skip like an empty pad.
            if (TrySkipEmptyAfterStuckNudge(step, layoutDestination, dist2d))
            {
                return false;
            }

            TryNavigateToward(
                destination,
                OpenTreasureCofferChain.PreferredOpenDistance,
                OpenTreasureCofferChain.PathArrivalRange);
            TryRecoverFromStuckWalk(step, StepDistance);
            return false;
        }

        ClearEmptyPadCandidate();

        // Opened / looted (including VBM AutoOpen) — do not keep pathing at a dead coffer.
        if (OpenTreasureCofferChain.IsOpenedOrLooted(present))
        {
            vnav.Stop();
            ResetStuckWatch();
            return true;
        }

        if (!TryNavigateToward(
                destination,
                OpenTreasureCofferChain.PreferredOpenDistance,
                OpenTreasureCofferChain.PathArrivalRange,
                skipIfOffMesh: false))
        {
            return false;
        }

        if (TryRecoverFromStuckWalk(step, StepDistance))
        {
            return false;
        }

        if (StepDistance > CofferOpenAttemptRadius)
        {
            return false;
        }

        // Match the open chain: mesh often parks just outside 2y. After a stuck nudge,
        // chest / prop collision can leave you 3–5y out — still interactable.
        const float StuckOpenSlack = 3.5f;
        float openSlack = stuckNudgeIssued
            ? StuckOpenSlack
            : OpenTreasureCofferChain.OpenAttemptSlack;
        if (dist2d > OpenTreasureCofferChain.PreferredOpenDistance + openSlack
            || !IsSameFloor(destination))
        {
            return false;
        }

        if (vnav.IsRunning() || vnav.IsPathfinding())
        {
            vnav.Stop();
            return false;
        }

        ResetStuckWatch();
        // Stay on Ninja + keep Hide requirement while threatened — gearset swap drops Hide and
        // nearby high-Knowledge mobs aggro before re-Hide.
        bool keepNinja = StillThreatenedForRemount();
        if (!keepNinja)
        {
            ClearNinjaHideRequirement();
        }

        ninjaHide.EndStealthForInteract(keepNinja);
        activeChain = chainManager.Manage(
            chains.Create($"TreasureHunt::Open({step.NodeId})")
                .Then<OpenTreasureCofferChain, TreasureOpenTarget>(present.Position)
        );

        return false;
    }

    /// <summary>Layout pad already has an opened/looted coffer — count it done and stop nav.</summary>
    private bool TryCompleteOpenedLayoutCoffer(Vector3 layoutDestination, uint nodeId)
    {
        IGameObject? present = FindTreasureForLayout(layoutDestination, nodeId);
        if (present == null || !OpenTreasureCofferChain.IsOpenedOrLooted(present))
        {
            return false;
        }

        vnav.Stop();
        ResetStuckWatch();
        return true;
    }

    private bool HandleReturnToBaseCamp()
    {
        StepDistance = 0f;
        IZone zone = zones.GetZone();
        bool inCombat = conditions[ConditionFlag.InCombat];

        // Return cannot cast in combat — walk toward camp until it drops, then cast.
        if (inCombat)
        {
            if (activeChain != null)
            {
                chainManager.CancelWhere(name => name.StartsWith("TreasureHunt::Return", StringComparison.Ordinal));
                activeChain = null;
            }

            SprintAssist.MaybeCast(movementConfig.SprintOnAetheryteApproach, zone.IsInBasecamp());

            if (!vnav.IsRunning() && !vnav.IsPathfinding())
            {
                Vector3 standOff = zone.GetMainAetheryte().GetCampStandOffPosition(player.Position);
                log.Debug("Treasure hunt: in combat after last coffer — walking toward camp until Return is usable");
                vnav.PathfindAndMoveCloseTo(standOff, false, AethernetNavigation.PathfindArrivalRadius);
            }

            return false;
        }

        if (vnav.IsRunning())
        {
            vnav.Stop();
        }

        if (conditions[ConditionFlag.Unconscious])
        {
            return false;
        }

        if (activeChain != null)
        {
            if (!activeChain.IsCompleted)
            {
                return false;
            }

            bool returned = activeChain.IsCompletedSuccessfully && zone.IsInBasecamp();
            activeChain = null;
            if (!returned)
            {
                return false;
            }
        }

        if (zone.IsInBasecamp())
        {
            // SimpleMove rejects a stacked move-to while Return's last pathfind is still
            // queued. Wait until that slot is free so the first camp pad actually starts.
            if (vnav.IsPathfinding())
            {
                return false;
            }

            return true;
        }

        activeChain = chainManager.Manage(
            ReturnToBaseCamp.Append(
                chains.Create("TreasureHunt::Return"),
                zones,
                conditions,
                gui,
                pathfinder,
                vnav));

        return false;
    }

    private bool HandleWalkToAethernet(HuntPathfinderStep step)
    {
        if (!Running)
        {
            vnav.Stop();
            return true;
        }

        AethernetData aethernet = ResolveAethernet(step.Aethernet);
        Vector3 crystal = aethernet.Position;
        Vector3 destination = aethernet.GetCampStandOffPosition(player.Position);
        StepDistance = player.Position.Distance2D(crystal);

        // Prefer Lifestream-ready (magenta) over raw crystal distance — stand-off may sit on the pad.
        if (zones.GetZone().IsWithinLifestreamRange(player.Position)
            || player.Position.Distance2D(destination) <= AethernetNavigation.PathfindArrivalRadius + 0.35f)
        {
            vnav.Stop();
            return true;
        }

        float arrival = AethernetNavigation.PathfindArrivalRadius;
        if (!TryNavigateToward(destination, arrival + 0.35f, arrival, skipIfOffMesh: false))
        {
            return false;
        }

        return false;
    }

    private bool HandleTeleportToAethernet(HuntPathfinderStep step)
    {
        StepDistance = 0f;

        if (activeChain != null)
        {
            if (!activeChain.IsCompleted)
            {
                return false;
            }

            bool teleported = activeChain.IsCompletedSuccessfully
                              && (activeChain.Result?.IsSuccess ?? false);
            activeChain = null;
            return teleported;
        }

        uint placeNameId = (uint)step.Aethernet;
        if (!zones.GetZone().IsUsableAethernetDestination(placeNameId))
        {
            log.Warning("Treasure hunt: skipping locked aethernet {Id}", placeNameId);
            return true;
        }

        activeChain = chainManager.Manage(
            chains.Create($"TreasureHunt::Teleport({placeNameId})")
                .Then<AethernetTeleportChain, uint>(placeNameId)
        );

        return false;
    }

    private void MaybeMount(Vector3 destination)
    {
        // Sight prep / WideText wait — remounting here fights DismountAssist every tick.
        if (waitingForSightCounts)
        {
            return;
        }

        if (ninjaHideRequired)
        {
            return;
        }

        if (ninjaHide.IsStealthed
            && !ninjaHide.TryEndStealthForTravel(StillThreatenedForRemount))
        {
            return;
        }

        // Shared skip (between-areas / aetheryte still targeted) — avoids post-TP "Invalid target."
        MountWait.TryCastIfNeeded(
            conditions,
            objects,
            destination,
            movementConfig.ShouldAutoMount,
            movementConfig.PreferredMountId,
            inBaseCamp: false);
    }

    /// <summary>Path/mount only after Hide is ready when required.</summary>
    /// <returns>False while still preparing Hide (caller should wait).</returns>
    private bool TryNavigateToward(
        Vector3 destination,
        float startPathBeyond,
        float arrivalRadius,
        bool skipIfOffMesh = true)
    {
        if (!ApplyNinjaHideGate())
        {
            return false;
        }

        if (DateTime.UtcNow < holdNavigateUntilUtc)
        {
            // Remount here cancels the stuck nudge before it can start.
            return true;
        }

        if (!TreasurePathing.TryResolvePathable(
                destination,
                player.Position.Y,
                vnav,
                skipIfOffMesh,
                out Vector3 pathTarget))
        {
            if (vnav.IsRunning() || vnav.IsPathfinding())
            {
                vnav.Stop();
            }

            log.Debug(
                "Treasure hunt: no navmesh near {Pos} — holding path until stuck recovery",
                destination);
            ClearNavigateIssue();
            ClearNavigateClosingLatch();
            MaybeMount(destination);
            return true;
        }

        // Navmesh snap can land several yalms from the layout point — finish the walk-in mounted when auto-mount is on.
        // Do not re-snap here: TryResolvePathable would return the same short landing forever.
        const float SnapArrivalSlack = 2f;
        bool snapOffset = destination.Distance2D(pathTarget) > SnapArrivalSlack;

        // New goal — drop the "closing on raw destination" latch from the previous pad/coffer.
        if (navigateClosingForDestination is not { } closingFor
            || closingFor.Distance2D(destination) > SnapArrivalSlack)
        {
            navigateClosingOnDestination = false;
            navigateClosingForDestination = destination;
        }

        Vector3 moveTarget = pathTarget;
        if (snapOffset)
        {
            bool nearSnap = player.Position.Distance2D(pathTarget) <= startPathBeyond + arrivalRadius;
            if (navigateClosingOnDestination || nearSnap)
            {
                // One-way: once we leave the mesh point for the real coffer/pad, stay on it.
                // Falling back to the snap tug-of-wars when the live object sits off-mesh.
                navigateClosingOnDestination = true;
                moveTarget = destination with { Y = player.Position.Y };
            }
        }

        // Interact only needs PreferredOpenDistance; demanding PathArrivalRange (1y) walks into
        // chest / prop collision ("invisible wall") while vnav still claims a path.
        float moveArrival = MathF.Max(arrivalRadius, startPathBeyond);

        // Same stand-off slack as aetheryte approach: without it, vnav completes at ~moveArrival
        // while needPath still wants < startPathBeyond, and we re-queue forever (~2.05y spam).
        const float ArrivalSlack = 0.5f;

        Vector3 arrivalPoint = snapOffset && navigateClosingOnDestination ? destination : moveTarget;
        float distToArrival = player.Position.Distance2D(arrivalPoint);
        bool needPath = !IsSameFloor(destination)
                        || distToArrival > startPathBeyond + ArrivalSlack;

        const float RepathDrift = 1.5f;
        bool drifted = lastNavigateTarget is not { } last
                       || last.Distance2D(moveTarget) > RepathDrift;

        // Parked on the issued move point — do not PathfindAndMove again every tick.
        if (needPath
            && !drifted
            && player.Position.Distance2D(moveTarget) <= moveArrival + ArrivalSlack)
        {
            needPath = false;
        }

        // Same dest: do not re-issue until the cooldown elapses. The old "grace only
        // before first follow" check re-queued every tick once vnav had moved and then
        // stopped against chest / prop collision (1805 loop).
        bool sameDestCooling = !drifted
                               && lastNavigateTarget is not null
                               && DateTime.UtcNow - lastNavigateIssuedUtc < SameDestRepathCooldown;

        if (needPath
            && !sameDestCooling
            && (drifted || (!vnav.IsRunning() && !vnav.IsPathfinding())))
        {
            lastNavigateTarget = moveTarget;
            lastNavigateIssuedUtc = DateTime.UtcNow;
            vnav.PathfindAndMoveCloseTo(moveTarget, false, moveArrival);
        }

        MaybeMount(moveTarget);
        return true;
    }

    private bool IsSameFloor(Vector3 destination) =>
        HuntDistances.IsSameFloor(player.Position, destination);

    /// <summary>
    ///     When enabled and a knowledge threat is in range: gearset → dismount → Hide; remount once the threat is clear.
    ///     Returns false while still preparing (caller should wait).
    /// </summary>
    private bool ApplyNinjaHideGate()
    {
        if (!config.UseNinjaHideOnDangerousRoutes)
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

        // Stop nav while preparing Hide; combat waits in EnsureReady.
        if (conditions[ConditionFlag.InCombat])
        {
            return true;
        }

        if (ninjaHide.EnsureReady(config.NinjaGearsetNumber))
        {
            // Best-effort speed buff — never blocks walking.
            if (config.UseOccultSprintWhileHidden)
            {
                ninjaHide.TryOccultSprintWhileHidden();
            }

            return true;
        }

        if (config.NinjaGearsetNumber <= 0 && !ninjaHide.IsNinja)
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
            config.KnowledgeHideOffset,
            config.KnowledgeThreatEnterDistance,
            config.KnowledgeThreatExitDistance);
    }

    private bool StillThreatenedForRemount() =>
        ninjaHideRouteGate.ShouldKeepStealthForThreats(
            objects,
            player.Position,
            config.KnowledgeHideOffset,
            config.KnowledgeThreatExitDistance);

    private void MaintainNinjaHideDuringInteract()
    {
        if (!config.UseNinjaHideOnDangerousRoutes || !ninjaHideRequired)
        {
            return;
        }

        if (conditions[ConditionFlag.InCombat])
        {
            return;
        }

        _ = ninjaHide.EnsureReady(config.NinjaGearsetNumber);
    }

    private void ClearNinjaHideRequirement()
    {
        ninjaHideRequired = false;
        ninjaHideRouteGate.Reset();
    }

    /// <summary>
    ///     True while still walking in from outside <see cref="TreasureConfig.EmptyPadTrustDistance"/>.
    ///     Inside that radius the empty-pad slider applies even if vnav is still running.
    /// </summary>
    private bool IsStillApproachingOutsideTrust(float dist2d) =>
        dist2d > config.EmptyPadTrustDistance;

    /// <summary>Another coffer streamed nearby — region is loaded enough for early empty-skip.</summary>
    private bool RegionProvesStream(Vector3 layoutDestination) =>
        player.Position.Distance2D(layoutDestination) <= HuntDistances.EmptyPadEarlySkipRadius
        && CountTreasuresNear(layoutDestination, HuntDistances.EmptyPadEarlySkipRadius) > 0;

    /// <summary>True when the player is close enough to trust that this pad has no live coffer.</summary>
    private bool CanTrustEmptyPad(Vector3 layoutDestination)
    {
        // Surface above a basement pad is "close" in 2D but not actually at the coffer.
        if (!IsSameFloor(layoutDestination))
        {
            return false;
        }

        float dist = player.Position.Distance2D(layoutDestination);

        if (dist <= config.EmptyPadTrustDistance)
        {
            return true;
        }

        // Further out, only skip if a neighbour coffer proves this region has streamed.
        return dist <= HuntDistances.EmptyPadEarlySkipRadius
               && CountTreasuresNear(layoutDestination, HuntDistances.EmptyPadEarlySkipRadius) > 0;
    }

    /// <summary>Treasure objects currently streamed within <paramref name="radius"/> of a point.</summary>
    private int CountTreasuresNear(Vector3 origin, float radius)
    {
        int count = 0;
        foreach (IGameObject obj in tickTreasures)
        {
            if (origin.Distance2D(obj.Position) <= radius)
            {
                count++;
            }
        }

        return count;
    }

    private bool ConfirmEmptyPad(uint nodeId)
    {
        DateTime now = DateTime.UtcNow;
        if (emptyPadCandidateNodeId != nodeId)
        {
            emptyPadCandidateNodeId = nodeId;
            emptyPadCandidateSinceUtc = now;
            return false;
        }

        return now - emptyPadCandidateSinceUtc >= HuntDistances.EmptyPadConfirmDelay;
    }

    private void ClearEmptyPadCandidate()
    {
        emptyPadCandidateNodeId = null;
        emptyPadCandidateSinceUtc = DateTime.MinValue;
    }

    private void ClearWalkLiveBind()
    {
        walkLiveBindNodeId = null;
        walkLiveBindPosition = null;
    }

    private void ClearNavigateClosingLatch()
    {
        navigateClosingOnDestination = false;
        navigateClosingForDestination = null;
    }

    private void ClearNavigateIssue()
    {
        lastNavigateTarget = null;
        lastNavigateIssuedUtc = DateTime.MinValue;
        holdNavigateUntilUtc = DateTime.MinValue;
    }

    /// <summary>Rebuild <see cref="tickTreasures"/> once per tick for pad matching.</summary>
    private void RefreshTickTreasures()
    {
        tickTreasures.Clear();
        foreach (IGameObject obj in objects)
        {
            if (obj is { ObjectKind: ObjectKind.Treasure, IsDead: false } && obj.IsValid())
            {
                tickTreasures.Add(obj);
            }
        }
    }

    private IGameObject? FindTreasureNear(Vector3 layoutDestination, float radius) =>
        GameObjectNearest.Find2D(tickTreasures, layoutDestination, radius);

    private IGameObject? FindUnopenedTreasureNear(Vector3 layoutDestination, float radius) =>
        GameObjectNearest.Find2D(
            tickTreasures,
            layoutDestination,
            radius,
            static o => !OpenTreasureCofferChain.IsOpenedOrLooted(o));

    /// <summary>
    /// Live coffer owned by this layout node (not a neighbor pad in the next segment).
    /// </summary>
    private IGameObject? FindTreasureForLayout(Vector3 layoutDestination, uint nodeId)
    {
        // Prefer unopened — an opened ghost on the pad must not hide a live silver neighbor match.
        IGameObject? close = FindUnopenedTreasureNear(layoutDestination, HuntDistances.LayoutProximityRadius)
                             ?? FindTreasureNear(layoutDestination, HuntDistances.LayoutProximityRadius);
        if (close != null && LiveCofferBelongsToLayout(close, nodeId, layoutDestination))
        {
            return close;
        }

        IGameObject? drifted = FindUnopenedTreasureNear(layoutDestination, HuntDistances.MatchRadius)
                               ?? FindTreasureNear(layoutDestination, HuntDistances.MatchRadius);
        if (drifted == null || !LiveCofferBelongsToLayout(drifted, nodeId, layoutDestination))
        {
            return null;
        }

        return drifted;
    }

    /// <summary>Pad owns coffer within LayoutProximityRadius; else nearest layout wins.</summary>
    private bool LiveCofferBelongsToLayout(IGameObject live, uint nodeId, Vector3 layoutDestination)
    {
        float toThisPad = layoutDestination.Distance2D(live.Position);
        if (toThisPad <= HuntDistances.LayoutProximityRadius)
        {
            return true;
        }

        TreasureLayoutDatum nearest = default;
        float nearestDist = float.MaxValue;
        foreach (TreasureLayoutDatum layout in layoutTreasure)
        {
            float d = layout.Position.Distance2D(live.Position);
            if (d < nearestDist)
            {
                nearestDist = d;
                nearest = layout;
            }
        }

        if (nearest.Id != 0)
        {
            return nearest.Id == nodeId;
        }

        return false;
    }

    private bool IsUnsafeTreasureWindow()
    {
        TreasureRoutePolicy policy = zones.GetZone().GetTreasureRoutePolicy();
        int eorzeaMinute = TreasureRoutePolicy.GetEorzeaMinuteOfDay(DateTimeOffset.UtcNow);
        if (policy.IsAshkinPeriod(eorzeaMinute))
        {
            return true;
        }

        byte weatherId = GetCurrentWeatherId();
        return weatherId != 0 && policy.IsUnsafeWeather(weatherId);
    }

    private static unsafe byte GetCurrentWeatherId()
    {
        FFXIVClientStructs.FFXIV.Client.Graphics.Environment.EnvManager* env =
            FFXIVClientStructs.FFXIV.Client.Graphics.Environment.EnvManager.Instance();
        return env == null ? (byte)0 : env->ActiveWeather;
    }

    private List<uint> GetValidNodes(int maxLevel)
    {
        IZone zone = zones.GetZone();
        List<TreasureData> treasureData = zone.GetTreasureData();
        IReadOnlyList<CrowdsourcedCofferCandidate> liveSpots = cofferLocations.GetAcceptedForCurrentZone();
        bool hasAuthoredPositions = treasureData.Exists(d => d.Position.HasValue);
        bool hasCrowdsourced = liveSpots.Count > 0;

        if (hasAuthoredPositions || hasCrowdsourced)
        {
            return layoutTreasure
                .Where(t => MatchesHuntCofferFilter(t.ModelId))
                .Where(t => !TreasureHuntPathOverrides.IsUnreachable(zone.ZoneId, t.Id))
                .Where(t => IsHuntPadAllowed(t, treasureData, liveSpots, maxLevel, hasAuthoredPositions, hasCrowdsourced))
                .Select(t => t.Id)
                .ToList();
        }

        return treasureData
            .Where(node => node.Level <= maxLevel)
            .Select(node => (uint)node.Id)
            .Where(id => !TreasureHuntPathOverrides.IsUnreachable(zone.ZoneId, id))
            .Where(id =>
            {
                return TryGetLayout(id, out TreasureLayoutDatum layout)
                       && MatchesHuntCofferFilter(layout.ModelId);
            })
            .ToList();
    }

    /// <summary>Authored (level-gated) or crowdsourced pad — union so both catalogs help the hunt.</summary>
    private static bool IsHuntPadAllowed(
        TreasureLayoutDatum layout,
        List<TreasureData> treasureData,
        IReadOnlyList<CrowdsourcedCofferCandidate> liveSpots,
        int maxLevel,
        bool hasAuthoredPositions,
        bool hasCrowdsourced)
    {
        if (hasAuthoredPositions || hasCrowdsourced)
        {
            bool onAuthored = hasAuthoredPositions
                              && treasureData.Any(d => d.Matches(layout.Id, layout.Position));
            bool onCrowd = hasCrowdsourced
                           && liveSpots.Any(c =>
                               Vector3.DistanceSquared(c.Position, layout.Position)
                               <= CofferLocationSyncService.MatchRadiusSq);
            if (!onAuthored && !onCrowd)
            {
                return false;
            }
        }

        if (!TreasureData.TryResolveLevel(layout.Id, layout.Position, treasureData, out int padLevel))
        {
            // Shared-only pad with no baked level — only when user left the cap at 50 (no limit).
            return maxLevel >= 50;
        }

        return padLevel <= maxLevel;
    }

    private bool MatchesHuntCofferFilter(uint sgbId) =>
        !config.HuntSilverChestsOnly || sgbId == TreasureCoffer.SilverSgbId;

    private List<uint> GetValidNodesForNextPlan()
    {
        int maxLevel = maxLevelOverrideForNextRun ?? config.HuntMaxLevel;
        List<uint> validNodes = GetValidNodes(maxLevel)
            .Where(id => !excludedNodeIdsForNextRun.Contains(id))
            .Where(id => !checkedNodeIds.Contains(id))
            .Where(id => !IsLayoutCofferOpened(id))
            .ToList();

        if (validNodes.Count > 0 || excludedNodeIdsForNextRun.Count == 0)
        {
            return validNodes;
        }

        log.Info("Pots & Treasure visited every known treasure node; starting a fresh treasure route.");
        excludedNodeIdsForNextRun.Clear();
        return GetValidNodes(maxLevel)
            .Where(id => !checkedNodeIds.Contains(id))
            .Where(id => !IsLayoutCofferOpened(id))
            .ToList();
    }

    /// <summary>True when a live opened/looted coffer sits on this layout node (skip when resuming).</summary>
    private bool IsLayoutCofferOpened(uint nodeId)
    {
        if (!TryGetLayout(nodeId, out TreasureLayoutDatum layout))
        {
            return false;
        }

        IGameObject? present = FindTreasureForLayout(layout.Position, nodeId);
        return present != null && OpenTreasureCofferChain.IsOpenedOrLooted(present);
    }

    private TreasureHuntPathfinder? CreatePathPlanner()
    {
        cofferLocations.EnsureFreshForHunt();

        layoutTreasure.Clear();
        layoutById.Clear();

        unsafe
        {
            LayoutManager* layout = LayoutWorld.Instance()->ActiveLayout;
            if (layout == null)
            {
                log.Warning("No active layout for treasure hunt");
                return null;
            }

            if (!layout->InstancesByType.TryGetValue(InstanceType.Treasure, out Pointer<StdMap<ulong, Pointer<ILayoutInstance>>> mapPtr, false))
            {
                log.Warning("No active treasure layout instances");
                return null;
            }

            List<TreasureData> treasureData = zones.GetZone().GetTreasureData();
            bool hasPositionData = treasureData.Exists(d => d.Position.HasValue);
            IReadOnlyList<CrowdsourcedCofferCandidate> liveSpots = cofferLocations.GetAcceptedForCurrentZone();
            bool hasCrowdsourced = liveSpots.Count > 0;

            foreach(ILayoutInstance* instance in mapPtr.Value->Values)
            {
                Transform* transform = instance->GetTransformImpl();
                Vector3 position = transform->Translation;
                if (position.Y <= -10f && !hasPositionData && !hasCrowdsourced)
                {
                    continue;
                }

                uint treasureRowId = Unsafe.Read<uint>((byte*)instance + 0x30);
                uint sgbId = data.GetExcelSheet<TreasureSheet>().GetRow(treasureRowId).SGB.RowId;
                if (!TreasureCoffer.IsBronzeOrSilverSgb(sgbId))
                {
                    continue;
                }

                TreasureData? authored = hasPositionData
                    ? treasureData.FirstOrDefault(d => d.Matches(treasureRowId, position))
                    : null;
                bool matchAuthored = authored != null;
                bool matchCrowd = hasCrowdsourced
                    && liveSpots.Any(c =>
                        Vector3.DistanceSquared(c.Position, position) <= CofferLocationSyncService.MatchRadiusSq);

                // Union: baked map and/or shared catalog. Neither → take every bronze/silver layout pad.
                if ((hasPositionData || hasCrowdsourced) && !matchAuthored && !matchCrowd)
                {
                    continue;
                }

                // Layout transform Y is often bogus (reveal altitude / inside floor). Prefer baked coords.
                if (authored?.Position is { } bakedPosition)
                {
                    position = bakedPosition;
                }

                layoutTreasure.Add(new(treasureRowId, position, sgbId));
            }
        }

        MergeBakedTreasurePads(zones.GetZone().GetTreasureData());
        MergeCrowdsourcedTreasurePads(cofferLocations.GetAcceptedForCurrentZone());

        if (layoutTreasure.Count == 0)
        {
            log.Warning("No treasure layout nodes found for hunt");
            return null;
        }

        layoutTreasure.Sort((a, b) => a.Id.CompareTo(b.Id));
        RebuildLayoutIndex();

        IZone zone = zones.GetZone();
        TreasureHuntPathfinder planner = new(
            zone.ZoneId,
            plugin,
            layoutTreasure,
            log
        );
        RefreshAuthoredSegmentCache(planner);
        return planner;
    }

    private void RebuildLayoutIndex()
    {
        layoutById.Clear();
        foreach (TreasureLayoutDatum layout in layoutTreasure)
        {
            layoutById[layout.Id] = layout;
        }
    }

    /// <summary>Layout pad for a node id, or false when the snapshot no longer has it.</summary>
    private bool TryGetLayout(uint nodeId, out TreasureLayoutDatum layout) =>
        layoutById.TryGetValue(nodeId, out layout);

    /// <summary>Fill baked pads missing from the active layout snapshot.</summary>
    private void MergeBakedTreasurePads(List<TreasureData> treasureData)
    {
        HashSet<uint> present = layoutTreasure.Select(t => t.Id).ToHashSet();
        int added = 0;
        foreach (TreasureData pad in treasureData)
        {
            if (pad.Position is not { } baked || !present.Add((uint)pad.Id))
            {
                continue;
            }

            // Model unknown until layout loads — treat as bronze so silver-only skips them.
            layoutTreasure.Add(new((uint)pad.Id, baked, TreasureCoffer.BronzeSgbId));
            added++;
        }

        if (added > 0)
        {
            log.Debug("Treasure hunt: merged {Count} baked pad(s) not in active layout", added);
        }
    }

    /// <summary>Fill shared-catalog pads missing from the layout snapshot (by position).</summary>
    private void MergeCrowdsourcedTreasurePads(IReadOnlyList<CrowdsourcedCofferCandidate> liveSpots)
    {
        if (liveSpots.Count == 0)
        {
            return;
        }

        int added = 0;
        foreach (CrowdsourcedCofferCandidate spot in liveSpots)
        {
            if (layoutTreasure.Any(t =>
                    Vector3.DistanceSquared(t.Position, spot.Position) <= CofferLocationSyncService.MatchRadiusSq))
            {
                continue;
            }

            if (layoutTreasure.Any(t => t.Id == spot.DataId))
            {
                continue;
            }

            layoutTreasure.Add(new(spot.DataId, spot.Position, TreasureCoffer.BronzeSgbId));
            added++;
        }

        if (added > 0)
        {
            log.Info("Treasure hunt: merged {Count} shared pad(s) not in active layout", added);
        }
    }

    private void RefreshAuthoredSegmentCache(IHuntRoutePlanner planner)
    {
        authoredNodeSegments.Clear();
        authoredNodeOrder.Clear();
        foreach (TreasureLayoutDatum layout in layoutTreasure)
        {
            if (planner.TryGetNodeSegment(layout.Id) is string segment)
            {
                authoredNodeSegments[layout.Id] = segment;
            }

            if (planner.TryGetNodeOrderIndex(layout.Id) is int order)
            {
                authoredNodeOrder[layout.Id] = order;
            }
        }

        log.Debug(
            "Treasure hunt authored segments cached: {Count} pads",
            authoredNodeSegments.Count);
    }

    /// <summary>
    ///     Segment this plan is working. Null when the zone has no authored route.
    /// </summary>
    private string? TryGetCurrentSegment(IReadOnlyList<uint> remaining, uint? entryNodeId)
    {
        if (authoredNodeSegments.Count == 0)
        {
            return null;
        }

        if (entryNodeId is uint entry && authoredNodeSegments.TryGetValue(entry, out string? entrySegment))
        {
            return entrySegment;
        }

        // Mid-route: segment of the next WalkToNode (skip travel hops).
        for (int i = Math.Max(StepIndex, 0); i < steps.Count; i++)
        {
            if (steps[i].Type == HuntPathfinderStepType.WalkToNode
                && authoredNodeSegments.TryGetValue(steps[i].NodeId, out string? heading))
            {
                return heading;
            }
        }

        return TryGetResumeNode(remaining) is uint resume
            ? authoredNodeSegments.GetValueOrDefault(resume)
            : null;
    }

    /// <summary>Resume pad after LastCheckedNodeId (wrap); avoids the wrong segment near camp.</summary>
    private uint? TryGetResumeNode(IReadOnlyList<uint> remaining)
    {
        int after = LastCheckedNodeId is uint last && authoredNodeOrder.TryGetValue(last, out int lastOrder)
            ? lastOrder
            : -1;

        uint? next = null;
        int nextOrder = int.MaxValue;
        uint? earliest = null;
        int earliestOrder = int.MaxValue;

        foreach (uint id in remaining)
        {
            if (!authoredNodeOrder.TryGetValue(id, out int order))
            {
                continue;
            }

            if (order < earliestOrder)
            {
                earliestOrder = order;
                earliest = id;
            }

            if (order > after && order < nextOrder)
            {
                nextOrder = order;
                next = id;
            }
        }

        return next ?? earliest;
    }

    /// <summary>
    /// First remaining pad in authored order inside the segment — peel must not jump past this.
    /// </summary>
    private uint? TryGetAuthoredFrontier(IEnumerable<uint> remaining, string segmentId)
    {
        uint? best = null;
        int bestOrder = int.MaxValue;
        foreach (uint id in remaining)
        {
            // Fail closed: no segment → not frontier (matches divert filter).
            if (authoredNodeSegments.GetValueOrDefault(id) != segmentId)
            {
                continue;
            }

            if (!authoredNodeOrder.TryGetValue(id, out int order) || order >= bestOrder)
            {
                continue;
            }

            bestOrder = order;
            best = id;
        }

        return best;
    }

    private List<uint> FilterNearbyPrefixToAuthoredFrontier(
        List<uint> nearbyPrefix,
        IReadOnlyList<uint> validNodes,
        string segmentId)
    {
        if (nearbyPrefix.Count == 0 || authoredNodeOrder.Count == 0)
        {
            return nearbyPrefix;
        }

        if (TryGetAuthoredFrontier(validNodes, segmentId) is not uint frontier)
        {
            return nearbyPrefix;
        }

        // Strict: only the next remaining pad — never re-prefix passed (checked) lives.
        return nearbyPrefix.Where(id => id == frontier).ToList();
    }

    private bool ShouldReturnAfterHunt()
    {
        if (!config.ReturnToBaseCampAfterHunt)
        {
            return false;
        }

        // IsInBasecamp is a generous radius (CampRadius), so the hunt can finish "at camp" while the
        // player is most of that distance away — which reads as "it played the sound but never
        // returned me". Say which check declined, so that case is distinguishable from the others.
        if (zones.GetZone().IsInBasecamp())
        {
            log.Debug(
                "Treasure hunt: no Return after hunt — already within the base camp radius ({Distance:F0}y from the aetheryte)",
                player.Position.Distance2D(zones.GetZone().GetAetherytePosition()));
            return false;
        }

        if (steps.Count > 0 && steps[^1].Type == HuntPathfinderStepType.ReturnToBaseCamp)
        {
            log.Debug("Treasure hunt: no Return after hunt — the route already ends with one");
            return false;
        }

        return true;
    }

    /// <summary>
    ///     Queue Return before ending the session. The last coffer often replans to an empty route
    ///     while still in combat — ending immediately skipped the walk-to-camp fallback and left
    ///     Return uncastable.
    /// </summary>
    private bool TryQueueReturnAfterHunt(string reason)
    {
        if (!ShouldReturnAfterHunt())
        {
            return false;
        }

        steps.Add(HuntPathfinderStep.ReturnToBaseCamp());
        if (StepIndex > steps.Count - 1)
        {
            StepIndex = steps.Count - 1;
        }

        log.Debug("Treasure hunt: Return to camp queued ({Reason})", reason);
        return true;
    }

    private void EnsureWalkVias(HuntPathfinderStep step)
    {
        if (walkViaStepIndex == StepIndex)
        {
            return;
        }

        walkViaStepIndex = StepIndex;
        walkViaIndex = 0;
        walkVias.Clear();
        ResetViaStuckWatch();

        ZoneId zoneId = zones.GetZone().ZoneId;

        // Departure vias from the previous pad (LastCheckedNodeId after replan).
        uint? previousNodeId = null;
        for (int i = StepIndex - 1; i >= 0; i--)
        {
            HuntPathfinderStep prev = steps[i];
            if (prev.Type != HuntPathfinderStepType.WalkToNode)
            {
                continue;
            }

            previousNodeId = prev.NodeId;
            break;
        }

        previousNodeId ??= LastCheckedNodeId;

        if (previousNodeId is uint prevId
            && TreasureHuntPathOverrides.TryGetDeparture(zoneId, prevId, out IReadOnlyList<Vector3> departure))
        {
            walkVias.AddRange(departure);
        }

        if (TreasureHuntPathOverrides.TryGetApproach(zoneId, step.NodeId, out IReadOnlyList<Vector3> approach))
        {
            walkVias.AddRange(approach);
        }

        // Skip vias we are already on (e.g. resumed mid-route next to the safe spot).
        while (walkViaIndex < walkVias.Count
               && player.Position.Distance2D(walkVias[walkViaIndex]) <= 3f)
        {
            walkViaIndex++;
        }

        if (walkVias.Count > 0)
        {
            log.Debug(
                "Treasure hunt: {Count} via(s) for node {NodeId} (index {Index})",
                walkVias.Count,
                step.NodeId,
                walkViaIndex);
        }
    }

    private AethernetData ResolveAethernet(HuntAethernet aethernet)
    {
        uint placeNameId = (uint)aethernet;
        return zones.GetZone().GetAetherytes().First(a => a.Id == placeNameId);
    }

    private void CompleteHunt()
    {
        CaptureCompletedRun();
        PlayHuntCompleteSound();
        Teardown();
    }

    private void CaptureCompletedRun()
    {
        if (!ManagedByPotsTreasure)
        {
            return;
        }

        lastCompletedRunNodeIds.Clear();
        foreach (uint nodeId in checkedNodeIds)
        {
            lastCompletedRunNodeIds.Add(nodeId);
        }
    }

    private void PlayHuntCompleteSound()
    {
        if (!config.PlaySoundOnHuntComplete)
        {
            return;
        }

        sounds.Play(config.HuntCompleteSound);
    }

    private void StopDueToLeavingOccultCrescent()
    {
        bool announceStandalone = !ManagedByPotsTreasure && !ManagedByIllegalModeFiller && !ManagedByMobFarmer;
        log.Info(
            "Left Occult Crescent — stopping treasure hunt (pots={Pots}, filler={Filler}, farmer={Farmer})",
            ManagedByPotsTreasure,
            ManagedByIllegalModeFiller,
            ManagedByMobFarmer);
        Teardown();
        if (announceStandalone)
        {
            BocchiChat.Print(chat, uiConfig, translator.T(".treasure.off_left_zone"));
        }
    }

    private void Teardown()
    {
        bool wasManagedByPotsTreasure = ManagedByPotsTreasure;
        bool wasStandalone = Running && !wasManagedByPotsTreasure && !ManagedByIllegalModeFiller && !ManagedByMobFarmer;
        bool wasIllegalFiller = ManagedByIllegalModeFiller;
        bool wasMobFarmer = ManagedByMobFarmer;

        Running = false;
        Paused = false;
        planningRoute = false;
        pendingStartSight = false;
        waitingForSightCounts = false;
        sessionStartSightArmed = false;
        sightCastUtc = DateTime.MinValue;
        locationsSinceLastSight = 0;
        ClearNinjaHideRequirement();
        pendingPreferStartNode = null;
        pendingRepathNearPlayer = false;
        pendingSessionCampReturn = false;
        pendingEntryNodeId = null;
        authoredNodeSegments.Clear();
        authoredNodeOrder.Clear();
        ClearEmptyPadCandidate();
        ClearWalkLiveBind();
        ClearNavigateClosingLatch();
        ninjaHide.RestorePreviousGearsetIfNeeded();
        walkViaStepIndex = -1;
        walkViaIndex = 0;
        walkVias.Clear();
        ResetViaStuckWatch();

        SoftStopMovement();

        stopwatch.Reset();
        StepIndex = 0;
        StepDistance = 0f;
        LastCheckedNodeId = null;
        ManagedByPotsTreasure = false;
        ManagedByIllegalModeFiller = false;
        ManagedByMobFarmer = false;
        checkedNodeIds.Clear();
        excludedNodeIdsForNextRun.Clear();
        maxLevelOverrideForNextRun = null;
        if (!wasManagedByPotsTreasure)
        {
            lastCompletedRunNodeIds.Clear();
        }

        layoutTreasure.Clear();
        layoutById.Clear();
        tickTreasures.Clear();
        pathPlanner = null;
        stuckSkippedNodeIds.Clear();
        pandoraAutoOpen.Release();

        if (wasStandalone || wasIllegalFiller || wasMobFarmer)
        {
            modeGuard.NotifyTreasureHuntEnded();
        }
    }
}
