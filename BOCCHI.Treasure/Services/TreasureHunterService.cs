using BOCCHI.Common;
using BOCCHI.Common.Config;
using BOCCHI.Common.Data.Aethernet;
using BOCCHI.Common.Data.SupportJobs;
using BOCCHI.Common.Data.Zones;
using BOCCHI.Common.Data.Zones.Graph;
using BOCCHI.Common.Services;
using BOCCHI.Treasure.ChainRecipes;
using BOCCHI.Treasure.Data;
using BOCCHI.Treasure.Hunt;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using ECommons.Throttlers;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.LayoutEngine;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.Interop;
using FFXIVClientStructs.STD;
using Ocelot.Actions;
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
    AutomatorConfig automatorConfig,
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
    PandoraAutoOpenHold pandoraAutoOpen
) : ITreasureHunter, IOnUpdate, IOnStop
{
    /// <summary>Start open attempts once this close to the coffer (yalms).</summary>
    private const float CofferOpenAttemptRadius = 75f;

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
    private const float StuckDetectionMinDistance = OpenTreasureCofferChain.MaxInteractRange;

    private readonly List<TreasureLayoutDatum> layoutTreasure = [];
    private readonly List<HuntPathfinderStep> steps = [];
    private readonly HashSet<uint> checkedNodeIds = [];
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
    private HashSet<uint> excludedNodeIdsForNextRun = [];
    private int? maxLevelOverrideForNextRun;
    private uint? stuckWatchNodeId;
    private float stuckWatchBestDistance = float.MaxValue;
    private DateTime stuckWatchStartedUtc = DateTime.MinValue;
    private bool stuckNudgeIssued;
    private uint? emptyPadCandidateNodeId;
    private DateTime emptyPadCandidateSinceUtc = DateTime.MinValue;

    /// <summary>
    /// Empty-skip at 100y only if we walked toward this pad from outside that radius.
    /// Prevents peel-off in a dense cluster from cascade-skipping every nearby empty pad.
    /// </summary>
    private uint? emptyPadApproachNodeId;
    private bool emptyPadSawOutsideSkipRadius;

    /// <summary>Force this pad as TSP start on the next plan (Nearby divert / reclaim).</summary>
    private uint? pendingPreferStartNode;

    /// <summary>South Horn red/blue (etc.): finish this half before switching after empty skips.</summary>
    private string? authoredStickyHalf;

    /// <summary>South Horn session start: prepend Return before first walk (cleared after first plan).</summary>
    private bool pendingSessionCampReturn;

    /// <summary>Prepend Return once when sticky half switches (red ↔ blue).</summary>
    private bool pendingHalfSwitchReturn;

    /// <summary>Node → half from the last loaded authored route (for divert while planner is null).</summary>
    private readonly Dictionary<uint, string> authoredNodeHalves = [];

    /// <summary>Node → authored order index (for sticky half: do not peel ahead of the route).</summary>
    private readonly Dictionary<uint, int> authoredNodeOrder = [];

    /// <summary>Hysteresis: Hide required until threats leave exit distance.</summary>
    private bool ninjaHideRequired;

    /// <summary>Via-points for the current WalkToNode (departure of previous + approach of current).</summary>
    private readonly List<Vector3> walkVias = [];

    private int walkViaIndex;
    private int walkViaStepIndex = -1;
    private int viaStuckIndex = -1;
    private float viaStuckBestDistance = float.MaxValue;
    private DateTime viaStuckStartedUtc = DateTime.MinValue;

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
            return;
        }

        if (conditions[ConditionFlag.Unconscious])
        {
            SoftStopMovement();
            return;
        }

        if (config.SkipUnsafeTreasureWindows && IsUnsafeTreasureWindow())
        {
            return;
        }

        if (!IsVnavReady)
        {
            return;
        }

        if (activeChain is { IsCompleted: false })
        {
            return;
        }

        if (pathPlanner != null)
        {
            if (pathPlanner.State != HuntPathfinderState.FileLoaded)
            {
                return;
            }

            if (!planningRoute)
            {
                return;
            }

            planningRoute = false;
            List<uint> validNodes = GetValidNodesForNextPlan();
            if (pendingPreferStartNode is uint preferLatch
                && !validNodes.Contains(preferLatch)
                && layoutTreasure.Any(t => t.Id == preferLatch))
            {
                // Divert latch — pad may have been checked empty while still live.
                checkedNodeIds.Remove(preferLatch);
                validNodes.Insert(0, preferLatch);
            }

            steps.Clear();
            List<uint> nearbyPrefix = [];
            // South Horn: exact authored order only — no Nearby peel / prefer-prefix detours.
            if (!UseStrictAuthoredRoute)
            {
                nearbyPrefix = FindAllLiveNearbyPreferNodes(validNodes);
                if (pendingPreferStartNode is uint latch && validNodes.Contains(latch))
                {
                    nearbyPrefix.Remove(latch);
                    nearbyPrefix.Insert(0, latch);
                }
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

            string? stickyHalf = authoredStickyHalf;
            if (stickyHalf != null && !pathPlanner.HalfHasRemaining(stickyHalf, validNodes))
            {
                string? nextHalf = pathPlanner.TryGetAlternateHalf(stickyHalf, validNodes);
                log.Info(
                    "Treasure hunt finished sticky half {Half}; switching to {Next}",
                    stickyHalf,
                    nextHalf ?? "(none)");
                if (nextHalf != null)
                {
                    pendingHalfSwitchReturn = !zones.GetZone().IsInBasecamp();
                }

                authoredStickyHalf = stickyHalf = nextHalf;
            }

            // First plan / recovery: lock to last checked pad's half while it still has work.
            if (stickyHalf == null && LastCheckedNodeId is uint lastId)
            {
                string? lastHalf = pathPlanner.TryGetNodeHalf(lastId);
                if (lastHalf != null && pathPlanner.HalfHasRemaining(lastHalf, validNodes))
                {
                    stickyHalf = lastHalf;
                }
            }

            // Non-strict zones: Nearby peel-off only within sticky half / authored frontier.
            if (!UseStrictAuthoredRoute && stickyHalf != null)
            {
                string stickyNorm = stickyHalf;
                nearbyPrefix = nearbyPrefix
                    .Where(id => pathPlanner.TryGetNodeHalf(id) == stickyNorm)
                    .ToList();
                nearbyPrefix = FilterNearbyPrefixToAuthoredFrontier(nearbyPrefix, validNodes, stickyNorm);
            }

            steps.AddRange(
                pathPlanner
                    .FindPath(player.Position, validNodes, nearbyPrefix, stickyHalf)
                    .GetAwaiter()
                    .GetResult());

            if (pendingSessionCampReturn)
            {
                pendingSessionCampReturn = false;
                if (steps.Count == 0 || steps[0].Type != HuntPathfinderStepType.ReturnToBaseCamp)
                {
                    steps.Insert(0, HuntPathfinderStep.ReturnToBaseCamp());
                }

                log.Info("Treasure hunt: prepended session-start Return to base camp");
            }

            if (pendingHalfSwitchReturn)
            {
                pendingHalfSwitchReturn = false;
                if (steps.Count == 0 || steps[0].Type != HuntPathfinderStepType.ReturnToBaseCamp)
                {
                    steps.Insert(0, HuntPathfinderStep.ReturnToBaseCamp());
                }

                log.Info(
                    "Treasure hunt: prepended Return before starting half {Half}",
                    authoredStickyHalf ?? "-");
            }

            if (pathPlanner.LastPrimaryHalf != null)
            {
                // Never let a partial layout snapshot flip sticky (e.g. camp red→blue).
                authoredStickyHalf ??= pathPlanner.LastPrimaryHalf;
            }

            RefreshAuthoredHalfCache(pathPlanner);
            pathPlanner = null;
            StepIndex = 0;
            // Treasure Sight once per session start (not on mid-hunt replans).
            // Armed here so it runs after session-start Return (Sight defers during Return/TP).
            if (!sessionStartSightArmed)
            {
                pendingStartSight = config.CastTreasureSightDuringHunt
                                    && SupportJobTreasureSight.CanCast(supportJobs);
                sessionStartSightArmed = true;
                log.Info(
                    "Treasure hunt: session start Sight {Armed} ({StepCount} step(s))",
                    pendingStartSight ? "armed" : "skipped",
                    steps.Count);
            }

            if (steps.Count == 0)
            {
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

        if (TryBeginTreasureSight())
        {
            return;
        }

        if (steps.Count == 0 || StepIndex >= steps.Count)
        {
            if (steps.Count > 0 && ShouldReturnAfterHunt())
            {
                steps.Add(HuntPathfinderStep.ReturnToBaseCamp());
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
                walkViaStepIndex = -1;
                walkVias.Clear();
                walkViaIndex = 0;
                StepDistance = 0f;
                RecalculateRoute();
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

        log.Info(
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
        pendingStartSight = false;
        waitingForSightCounts = false;
        sessionStartSightArmed = false;
        sightCastUtc = DateTime.MinValue;
        locationsSinceLastSight = 0;
        ninjaHideRequired = false;
        pendingPreferStartNode = null;
        authoredStickyHalf = null;
        pendingSessionCampReturn = false;
        pendingHalfSwitchReturn = false;
        authoredNodeHalves.Clear();
        authoredNodeOrder.Clear();
        checkedNodeIds.Clear();
        ClearEmptyPadCandidate();
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
            authoredStickyHalf = AlternateSouthHornStartHalf(config.LastSouthHornStartHalf);
            config.LastSouthHornStartHalf = authoredStickyHalf;
            configSaver.Save();
            pendingSessionCampReturn = !zone.IsInBasecamp();
            log.Info(
                "Treasure hunt South Horn: start half {Half}; camp Return {Return}",
                authoredStickyHalf,
                pendingSessionCampReturn ? "pending" : "skip (already in camp)");
        }

        Running = true;
        planningRoute = true;
        pandoraAutoOpen.Hold();
    }

    private static string AlternateSouthHornStartHalf(string? lastStartHalf)
    {
        if (string.Equals(lastStartHalf, "red", StringComparison.OrdinalIgnoreCase))
        {
            return "blue";
        }

        // null / blue / anything else → red
        return "red";
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

    public HuntPathfinderStep? GetCurrentStep()
    {
        if (StepIndex < 0 || StepIndex >= steps.Count)
        {
            return null;
        }

        return steps[StepIndex];
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

            int layoutIndex = layoutTreasure.FindIndex(t => t.Id == step.NodeId);
            if (layoutIndex < 0)
            {
                continue;
            }

            nodeId = step.NodeId;
            position = layoutTreasure[layoutIndex].Position;
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
        log.Info("Flagged treasure hunt resume coffer {NodeId} at {Position:f0}", nodeId, position);
        return true;
    }

    /// <summary>Stop movement/chains without clearing the planned route.</summary>
    private void SoftStopMovement()
    {
        chainManager.CancelWhere(name => name.StartsWith("TreasureHunt", StringComparison.Ordinal));
        pathfinder.Stop();
        vnav.Stop();
        activeChain = null;
        ResetStuckWatch();
    }

    private bool TryRecoverFromStuckWalk(HuntPathfinderStep step, float distance)
    {
        if (distance <= StuckDetectionMinDistance)
        {
            ResetStuckWatch();
            return false;
        }

        DateTime now = DateTime.UtcNow;
        if (stuckWatchNodeId != step.NodeId)
        {
            StartStuckWatch(step.NodeId, distance, now);
            return false;
        }

        // Progress toward the destination (not absolute movement) — circling a rock no longer resets forever.
        if (distance < stuckWatchBestDistance - StuckProgressThreshold)
        {
            stuckWatchBestDistance = distance;
            return false;
        }

        // #156: try a lateral nudge around geometry before giving up on the node.
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
        LastCheckedNodeId = step.NodeId;
        ResetStuckWatch();
        return RecalculateRoute();
    }

    private void TryIssueStuckNudge(HuntPathfinderStep step)
    {
        TreasureLayoutDatum layout = layoutTreasure.FirstOrDefault(t => t.Id == step.NodeId);
        Vector3 dest = layout.Id == step.NodeId ? layout.Position : player.Position;
        Vector3 toDest = dest - player.Position;
        toDest.Y = 0f;
        if (toDest.LengthSquared() < 0.25f)
        {
            toDest = new Vector3(1f, 0f, 0f);
        }

        Vector3 forward = Vector3.Normalize(toDest);
        Vector3 lateral = new(-forward.Z, 0f, forward.X);
        Vector3 nudge = player.Position + (lateral * 8f);
        nudge = new Vector3(nudge.X, player.Position.Y, nudge.Z);

        log.Info("Treasure hunt stuck near {NodeId} — nudging sideways around geometry (#156)", step.NodeId);
        pathfinder.Stop();
        vnav.Stop();
        vnav.PathfindAndMoveCloseTo(nudge, false, 1.5f);
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
            uint? nodeId = FindExclusiveLayoutNode(coffer.GetPosition(), valid, claimedPads);
            if (nodeId is not uint id)
            {
                continue;
            }

            claimedPads.Add(id);
            result.Add(id);
            log.Info(
                "Treasure hunt nearby peel candidate {NodeId} at {Distance:F1}y",
                id,
                distToPlayer);
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

    /// <summary>
    /// Nearest unclaimed layout pad for a live coffer.
    /// Nearby peel only uses tight proximity — matching out to MatchRadius made the hunt walk to a
    /// wrong pad, empty-skip around ~50–100y, and leave the live chest behind.
    /// </summary>
    private uint? FindExclusiveLayoutNode(Vector3 livePosition, HashSet<uint> validNodes, HashSet<uint> claimedPads) =>
        FindNearestUnclaimedLayoutNode(livePosition, validNodes, claimedPads, HuntDistances.LayoutProximityRadiusSq);

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

            TreasureLayoutDatum layout = layoutTreasure.FirstOrDefault(t => t.Id == nodeId);
            if (layout.Id != nodeId)
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
        // South Horn: follow treasure_route.json exactly — no Nearby detours.
        if (UseStrictAuthoredRoute)
        {
            return false;
        }

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

        HashSet<uint> candidates = GetDivertCandidateNodes(current);
        if (authoredStickyHalf != null)
        {
            // Stay on the sticky half — don't peel across red/blue.
            // Missing half tags are excluded (fail closed) so uncached pads can't leak through.
            string sticky = authoredStickyHalf;
            candidates.RemoveWhere(id =>
                !authoredNodeHalves.TryGetValue(id, out string? half) || half != sticky);

            // Only the next remaining authored pad — never peel back into checked/passed pads
            // or ahead to a later live chest on the same half.
            HashSet<uint> remaining = GetValidNodesForNextPlan().ToHashSet();
            if (TryGetAuthoredFrontier(remaining, sticky) is uint frontier)
            {
                candidates.RemoveWhere(id => id != frontier);
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

        TreasureLayoutDatum layout = layoutTreasure.FirstOrDefault(t => t.Id == nearbyId);
        if (layout.Id != nearbyId)
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
        else if (nearbyDist + HuntDistances.NearbyLiveDivertClearAdvantage >= currentDist
                 && currentDist <= HuntDistances.NearbyLiveDivertRange)
        {
            // Current goal is also "near" but empty/wrong pad — still require a clear win.
            return false;
        }
        // else: current goal is far / not live Nearby → always peel to the live Nearby.

        if (!EzThrottler.Throttle("TreasureHuntReprioritize", 1500))
        {
            return false;
        }

        // False empty-skip may have checked this pad while the coffer is still live.
        checkedNodeIds.Remove(nearbyId);
        pendingPreferStartNode = nearbyId;

        log.Info(
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
        TreasureLayoutDatum layout = layoutTreasure.FirstOrDefault(t => t.Id == nodeId);
        if (layout.Id != nodeId)
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
        foreach (TreasureLayoutDatum layout in layoutTreasure)
        {
            if (ids.Contains(layout.Id))
            {
                continue;
            }

            if (!MatchesHuntCofferFilter(layout.ModelId) || IsLayoutCofferOpened(layout.Id))
            {
                continue;
            }

            if (authored.Count > 0
                && !authored.Any(d => d.Level <= maxLevel && d.Matches(layout.Id, layout.Position)))
            {
                continue;
            }

            ids.Add(layout.Id);
        }

        return ids;
    }

    private float GetWalkNodeGoalDistance(uint nodeId)
    {
        TreasureLayoutDatum layout = layoutTreasure.FirstOrDefault(t => t.Id == nodeId);
        if (layout.Id != nodeId)
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

        // Defer while fighting — Sight dismounts + swaps PJ; remount fails in combat.
        if (conditions[ConditionFlag.InCombat])
        {
            return false;
        }

        SoftStopMovement();
        pendingStartSight = false;
        waitingForSightCounts = true;
        sightCastUtc = DateTime.UtcNow;
        locationsSinceLastSight = 0;

        log.Info(
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
        log.Info(
            "Treasure Sight refresh: {Bronze} bronze / {Silver} silver remaining; trimmed {Trimmed} nearby empty pad(s)",
            tracker.BronzeChests,
            tracker.SilverChests,
            trimmed);
        RecalculateRoute();
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
            TreasureLayoutDatum spot = layoutTreasure.FirstOrDefault(t => t.Id == nodeId);
            if (spot.Id != nodeId)
            {
                continue;
            }

            // Only trim pads we are standing on — do not wipe a dense cluster after Sight / peel.
            if (!IsLayoutPadEmpty(spot.Position, nodeId)
                || player.Position.Distance2D(spot.Position) > HuntDistances.EmptyPadRegionTrustRadius)
            {
                continue;
            }

            checkedNodeIds.Add(nodeId);
            trimmed++;
            log.Debug("Treasure Sight: trimming empty pad {NodeId} (within skip range)", nodeId);
        }

        return trimmed;
    }

    private bool ShouldAbortForNoChests()
    {
        if (!config.CastTreasureSightDuringHunt || !tracker.CountInitialised)
        {
            return false;
        }

        if (config.HuntSilverChestsOnly)
        {
            if (tracker.SilverChests > 0)
            {
                return false;
            }
        }
        else if (tracker.BronzeChests + tracker.SilverChests > 0)
        {
            return false;
        }

        // Still have route work that isn't the epilogue Return.
        for (int i = StepIndex; i < steps.Count; i++)
        {
            if (steps[i].Type != HuntPathfinderStepType.ReturnToBaseCamp)
            {
                return true;
            }
        }

        return false;
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

        EnsureWalkVias(step);
        if (walkViaIndex < walkVias.Count)
        {
            Vector3 via = walkVias[walkViaIndex];
            float viaDist = player.Position.Distance2D(via);
            StepDistance = viaDist;

            const float viaArrival = 2.5f;
            if (viaDist > viaArrival)
            {
                // Unreachable / off-mesh vias used to spam PathfindAndMoveCloseTo forever.
                if (TrySkipStuckVia(step.NodeId, viaDist))
                {
                    return false;
                }

                if (!vnav.IsRunning() && !vnav.IsPathfinding())
                {
                    vnav.PathfindAndMoveCloseTo(via, false, OpenTreasureCofferChain.PathArrivalRange);
                }

                MaybeMount(via);
                return false;
            }

            walkViaIndex++;
            ResetViaStuckWatch();
            vnav.Stop();
            return false;
        }

        Vector3 layoutDestination = layoutTreasure.First(t => t.Id == step.NodeId).Position;

        // Open finished: succeed, or skip the pad on failure.
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
            LastCheckedNodeId = step.NodeId;
            ResetStuckWatch();
            RecalculateRoute();
            return false;
        }

        IGameObject? present = FindTreasureForLayout(layoutDestination, step.NodeId);

        Vector3 destination = present?.Position ?? layoutDestination;

        float dist2d = player.Position.Distance2D(destination);
        StepDistance = dist2d;

        if (present == null)
        {
            if (CanTrustEmptyPad(layoutDestination, step.NodeId) && ConfirmEmptyPad(step.NodeId))
            {
                log.Info(
                    "Treasure hunt: no live coffer at layout {NodeId} — skipping and recalculating",
                    step.NodeId);
                checkedNodeIds.Add(step.NodeId);
                LastCheckedNodeId = step.NodeId;
                ClearEmptyPadCandidate();
                ResetStuckWatch();
                RecalculateRoute();
                return false;
            }

            // Still see a hunt coffer on radar — don't empty-skip (Nearby peel used to abandon ~50y out).
            if (HasUnopenedLiveHuntCofferNearPlayer(HuntDistances.NearbyLiveDivertRange))
            {
                IGameObject? loose = FindUnopenedTreasureNear(
                    layoutDestination,
                    HuntDistances.LayoutProximityRadius);
                if (loose != null)
                {
                    present = loose;
                    destination = loose.Position;
                    dist2d = player.Position.Distance2D(destination);
                    StepDistance = dist2d;
                }
                else
                {
                    if (!vnav.IsRunning() && !vnav.IsPathfinding()
                        && dist2d > OpenTreasureCofferChain.PreferredOpenDistance)
                    {
                        vnav.PathfindAndMoveCloseTo(destination, false, OpenTreasureCofferChain.PathArrivalRange);
                    }

                    MaybeMount(destination);
                    return false;
                }
            }
            if (present == null)
            {
                if (!vnav.IsRunning() && dist2d > OpenTreasureCofferChain.PreferredOpenDistance)
                {
                    vnav.PathfindAndMoveCloseTo(destination, false, OpenTreasureCofferChain.PathArrivalRange);
                }

                MaybeMount(destination);
                return false;
            }
        }

        ClearEmptyPadCandidate();

        if (!vnav.IsRunning()
            && !vnav.IsPathfinding()
            && dist2d > OpenTreasureCofferChain.PreferredOpenDistance)
        {
            vnav.PathfindAndMoveCloseTo(destination, false, OpenTreasureCofferChain.PathArrivalRange);
        }

        MaybeMount(destination);

        if (!ApplyNinjaHideGate())
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

        if (OpenTreasureCofferChain.IsOpenedOrLooted(present))
        {
            vnav.Stop();
            ResetStuckWatch();
            return true;
        }

        float dist3d = Vector3.Distance(player.Position, present.Position);
        if (dist3d > OpenTreasureCofferChain.PreferredOpenDistance)
        {
            return false;
        }

        if (vnav.IsRunning() || vnav.IsPathfinding())
        {
            vnav.Stop();
            return false;
        }

        ResetStuckWatch();
        activeChain = chainManager.Manage(
            chains.Create($"TreasureHunt::Open({step.NodeId})")
                .Then<OpenTreasureCofferChain, TreasureOpenTarget>(present.Position)
        );

        return false;
    }

    private bool HandleReturnToBaseCamp()
    {
        StepDistance = 0f;
        IZone zone = zones.GetZone();
        bool inCombat = conditions[ConditionFlag.InCombat];

        if (inCombat && !vnav.IsRunning())
        {
            SprintAssist.MaybeCast(automatorConfig.SprintOnAetheryteApproach, zone.IsInBasecamp());
            Vector3 standOff = zone.GetMainAetheryte().GetCampStandOffPosition(player.Position);
            vnav.PathfindAndMoveCloseTo(standOff, false, AethernetNavigation.PathfindArrivalRadius);
            return false;
        }

        if (!inCombat && vnav.IsRunning())
        {
            vnav.Stop();
        }

        if (inCombat)
        {
            return false;
        }

        if (conditions[ConditionFlag.Unconscious])
        {
            return false;
        }

        if (zone.IsInBasecamp())
        {
            return true;
        }

        if (activeChain != null)
        {
            if (!activeChain.IsCompleted)
            {
                return false;
            }

            bool returned = activeChain.IsCompletedSuccessfully && zone.IsInBasecamp();
            activeChain = null;
            return returned;
        }

        activeChain = chainManager.Manage(
            chains.Create("TreasureHunt::Return")
                .Then(_ =>
                {
                    if (Actions.Return.CanCast())
                    {
                        Actions.Return.Cast();
                    }

                    return StepResult.Success();
                }, "TreasureHunt::CastReturn")
                .WaitUntil(
                    _ =>
                    {
                        TryConfirmReturnDialog();
                        return ValueTask.FromResult(zones.GetZone().IsInBasecamp());
                    },
                    TimeSpan.FromSeconds(120),
                    TimeSpan.FromMilliseconds(250),
                    "TreasureHunt::WaitForBasecamp"
                )
        );

        return false;
    }

    private unsafe void TryConfirmReturnDialog()
    {
        // Death prompts also use SelectYesno — don't force-respawn while unconscious.
        if (conditions[ConditionFlag.Unconscious])
        {
            return;
        }

        if (!EzThrottler.Throttle("TreasureHunt::SelectYesno", 250))
        {
            return;
        }

        AddonSelectYesno* yesno = gui.GetAddonByName<AddonSelectYesno>("SelectYesno");
        if (yesno == null)
        {
            return;
        }

        ReturnYesNo.TryAccept(&yesno->AtkUnitBase);
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
        if (!vnav.IsRunning())
        {
            vnav.PathfindAndMoveCloseTo(destination, false, arrival);
        }

        MaybeMount(destination);

        if (!ApplyNinjaHideGate())
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
        activeChain = chainManager.Manage(
            chains.Create($"TreasureHunt::Teleport({placeNameId})")
                .Then<AethernetTeleportChain, uint>(placeNameId)
        );

        return false;
    }

    private void MaybeMount(Vector3 destination)
    {
        if (ninjaHideRequired || ninjaHide.IsStealthed)
        {
            return;
        }

        // Shared skip (between-areas / aetheryte still targeted) — avoids post-TP "Invalid target."
        MountWait.TryCastIfNeeded(
            conditions,
            objects,
            destination,
            automatorConfig.ShouldAutoMount,
            automatorConfig.PreferredMountId,
            inBaseCamp: false);
    }

    /// <summary>
    ///     When enabled and a knowledge threat is in range: gearset → dismount → Hide before continuing on foot.
    ///     Returns false while still preparing (caller should wait).
    /// </summary>
    private bool ApplyNinjaHideGate()
    {
        if (!config.UseNinjaHideOnDangerousRoutes)
        {
            ninjaHideRequired = false;
            return true;
        }

        UpdateNinjaHideRequired();

        if (!ninjaHideRequired)
        {
            ninjaHide.RestorePreviousGearsetIfNeeded();
            return true;
        }

        if (ninjaHide.EnsureReady(config.NinjaGearsetNumber))
        {
            return true;
        }

        // Stand still to cast Hide / swap gear; keep pathing while still mounted toward the threat.
        if (!ninjaHide.IsMounted)
        {
            vnav.Stop();
        }

        return false;
    }

    private void UpdateNinjaHideRequired()
    {
        if (KnowledgeThreat.TryFindIsleblazer(
                objects,
                player.Position,
                KnowledgeThreat.IsleblazerUnhideDistance,
                out _))
        {
            ninjaHideRequired = false;
            return;
        }

        if (KnowledgeThreat.TryGetPlayerForayLevel(objects) is not int foray)
        {
            ninjaHideRequired = false;
            return;
        }

        int hideAt = KnowledgeThreat.HideAtOrAbove(foray, config.KnowledgeHideOffset);
        float enter = config.KnowledgeThreatEnterDistance;
        float exit = Math.Max(config.KnowledgeThreatExitDistance, enter);

        if (ninjaHideRequired)
        {
            if (!KnowledgeThreat.TryFindThreat(objects, player.Position, hideAt, exit, out _, out _))
            {
                ninjaHideRequired = false;
            }

            return;
        }

        if (KnowledgeThreat.TryFindThreat(objects, player.Position, hideAt, enter, out _, out _))
        {
            ninjaHideRequired = true;
        }
    }

    private bool IsLayoutPadEmpty(Vector3 layoutDestination, uint nodeId) =>
        FindTreasureForLayout(layoutDestination, nodeId) == null;

    /// <summary>True when the player is close enough to trust that this pad has no live coffer.</summary>
    private bool CanTrustEmptyPad(Vector3 layoutDestination, uint nodeId)
    {
        float dist = player.Position.Distance2D(layoutDestination);

        if (emptyPadApproachNodeId != nodeId)
        {
            emptyPadApproachNodeId = nodeId;
            emptyPadSawOutsideSkipRadius = dist > HuntDistances.EmptyPadSkipRadius;
        }
        else if (dist > HuntDistances.EmptyPadSkipRadius)
        {
            emptyPadSawOutsideSkipRadius = true;
        }

        // Always trust emptiness when standing on the pad.
        if (dist <= HuntDistances.LayoutProximityRadius)
        {
            return true;
        }

        // Within load range only after walking in from outside — avoids cascade skips
        // when peel-off drops you in the middle of a dense authored cluster.
        return emptyPadSawOutsideSkipRadius && dist <= HuntDistances.EmptyPadSkipRadius;
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
        emptyPadApproachNodeId = null;
        emptyPadSawOutsideSkipRadius = false;
    }

    private IGameObject? FindTreasureNear(Vector3 layoutDestination, float radius)
    {
        return objects
            .Where(o => o is { ObjectKind: ObjectKind.Treasure, IsDead: false }
                        && o.IsValid()
                        && layoutDestination.Distance2D(o.Position) <= radius)
            .OrderBy(o => layoutDestination.Distance2D(o.Position))
            .FirstOrDefault();
    }

    private IGameObject? FindUnopenedTreasureNear(Vector3 layoutDestination, float radius)
    {
        return objects
            .Where(o => o is { ObjectKind: ObjectKind.Treasure, IsDead: false }
                        && o.IsValid()
                        && layoutDestination.Distance2D(o.Position) <= radius
                        && !OpenTreasureCofferChain.IsOpenedOrLooted(o))
            .OrderBy(o => layoutDestination.Distance2D(o.Position))
            .FirstOrDefault();
    }

    /// <summary>
    /// Live coffer owned by this layout node (not a neighbor pad on the other half).
    /// </summary>
    private IGameObject? FindTreasureForLayout(Vector3 layoutDestination, uint nodeId)
    {
        IGameObject? close = FindTreasureNear(layoutDestination, HuntDistances.LayoutProximityRadius);
        if (close != null && LiveCofferBelongsToLayout(close, nodeId, layoutDestination))
        {
            return close;
        }

        IGameObject? drifted = FindTreasureNear(layoutDestination, HuntDistances.MatchRadius);
        if (drifted == null || !LiveCofferBelongsToLayout(drifted, nodeId, layoutDestination))
        {
            return null;
        }

        return drifted;
    }

    /// <summary>
    /// True when this live coffer should be treated as belonging to <paramref name="nodeId"/>.
    /// Pads within proximity of this layout always count — a slightly closer neighbor must not
    /// steal the match or Nearby peel walks to a pad, empty-skips, and leaves the chest behind.
    /// </summary>
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
        List<TreasureData> treasureData = zones.GetZone().GetTreasureData();
        if (treasureData.Exists(d => d.Position.HasValue))
        {
            return layoutTreasure
                .Where(t => MatchesHuntCofferFilter(t.ModelId))
                .Where(t => treasureData.Any(d => d.Level <= maxLevel && d.Matches(t.Id, t.Position)))
                .Select(t => t.Id)
                .ToList();
        }

        return treasureData
            .Where(node => node.Level <= maxLevel)
            .Select(node => (uint)node.Id)
            .Where(id =>
            {
                TreasureLayoutDatum layout = layoutTreasure.FirstOrDefault(t => t.Id == id);
                return layout.Id == id && MatchesHuntCofferFilter(layout.ModelId);
            })
            .ToList();
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
        TreasureLayoutDatum layout = layoutTreasure.FirstOrDefault(t => t.Id == nodeId);
        if (layout.Id != nodeId)
        {
            return false;
        }

        IGameObject? present = FindTreasureForLayout(layout.Position, nodeId);
        return present != null && OpenTreasureCofferChain.IsOpenedOrLooted(present);
    }

    private TreasureHuntPathfinder? CreatePathPlanner()
    {
        layoutTreasure.Clear();

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

            foreach(ILayoutInstance* instance in mapPtr.Value->Values)
            {
                Transform* transform = instance->GetTransformImpl();
                Vector3 position = transform->Translation;
                if (position.Y <= -10f && !hasPositionData)
                {
                    continue;
                }

                uint treasureRowId = Unsafe.Read<uint>((byte*)instance + 0x30);
                uint sgbId = data.GetExcelSheet<TreasureSheet>().GetRow(treasureRowId).SGB.RowId;
                if (!TreasureCoffer.IsBronzeOrSilverSgb(sgbId))
                {
                    continue;
                }

                if (hasPositionData && !treasureData.Any(d => d.Matches(treasureRowId, position)))
                {
                    continue;
                }

                layoutTreasure.Add(new(treasureRowId, position, sgbId));
            }
        }

        MergeBakedTreasurePads(zones.GetZone().GetTreasureData());

        if (layoutTreasure.Count == 0)
        {
            log.Warning("No treasure layout nodes found for hunt");
            return null;
        }

        layoutTreasure.Sort((a, b) => a.Id.CompareTo(b.Id));

        IZone zone = zones.GetZone();
        TreasureHuntPathfinder planner = new(
            zone.ZoneId,
            plugin,
            layoutTreasure,
            log
        );
        RefreshAuthoredHalfCache(planner);
        return planner;
    }

    /// <summary>
    /// Layout only loads nearby pads — without baked positions, replans near camp drop the rest of
    /// the sticky half and the hunt flips to the other color. Fill gaps from zone treasure data.
    /// </summary>
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
            log.Info("Treasure hunt: merged {Count} baked pad(s) not in active layout", added);
        }
    }

    /// <summary>
    /// South Horn follows the authored red/blue route pad-by-pad — no Nearby peel-off.
    /// </summary>
    private bool UseStrictAuthoredRoute => zones.GetZone().ZoneId == ZoneId.SouthHorn;

    private void RefreshAuthoredHalfCache(IHuntRoutePlanner planner)
    {
        authoredNodeHalves.Clear();
        authoredNodeOrder.Clear();
        foreach (TreasureLayoutDatum layout in layoutTreasure)
        {
            if (planner.TryGetNodeHalf(layout.Id) is string half)
            {
                authoredNodeHalves[layout.Id] = half;
            }

            if (planner.TryGetNodeOrderIndex(layout.Id) is int order)
            {
                authoredNodeOrder[layout.Id] = order;
            }
        }

        log.Info(
            "Treasure hunt authored halves cached: {Count} pads, sticky={Sticky}",
            authoredNodeHalves.Count,
            authoredStickyHalf ?? "-");
    }

    /// <summary>
    /// First remaining pad in authored order on the sticky half — peel must not jump past this.
    /// </summary>
    private uint? TryGetAuthoredFrontier(IEnumerable<uint> remaining, string stickyHalf)
    {
        uint? best = null;
        int bestOrder = int.MaxValue;
        foreach (uint id in remaining)
        {
            if (authoredNodeHalves.TryGetValue(id, out string? half) && half != stickyHalf)
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
        string stickyHalf)
    {
        if (nearbyPrefix.Count == 0 || authoredNodeOrder.Count == 0)
        {
            return nearbyPrefix;
        }

        if (TryGetAuthoredFrontier(validNodes, stickyHalf) is not uint frontier)
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

        if (zones.GetZone().IsInBasecamp())
        {
            return false;
        }

        // Already appended the epilogue Return for this run.
        return steps.Count == 0 || steps[^1].Type != HuntPathfinderStepType.ReturnToBaseCamp;
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

        // Leave previous coffer through its safe exit before heading to the next.
        // After RecalculateRoute the finished pad is gone from steps — use LastCheckedNodeId.
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
            log.Info(
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
        bool announceStandalone = !ManagedByPotsTreasure && !ManagedByIllegalModeFiller;
        log.Info(
            "Left Occult Crescent — stopping treasure hunt (pots={Pots}, filler={Filler})",
            ManagedByPotsTreasure,
            ManagedByIllegalModeFiller);
        Teardown();
        if (announceStandalone)
        {
            BocchiChat.Print(chat, uiConfig, translator.T(".treasure.off_left_zone"));
        }
    }

    private void Teardown()
    {
        bool wasManagedByPotsTreasure = ManagedByPotsTreasure;
        bool wasStandalone = Running && !wasManagedByPotsTreasure && !ManagedByIllegalModeFiller;
        bool wasIllegalFiller = ManagedByIllegalModeFiller;

        Running = false;
        Paused = false;
        planningRoute = false;
        pendingStartSight = false;
        waitingForSightCounts = false;
        sessionStartSightArmed = false;
        sightCastUtc = DateTime.MinValue;
        locationsSinceLastSight = 0;
        ninjaHideRequired = false;
        pendingPreferStartNode = null;
        authoredStickyHalf = null;
        pendingSessionCampReturn = false;
        pendingHalfSwitchReturn = false;
        authoredNodeHalves.Clear();
        authoredNodeOrder.Clear();
        ClearEmptyPadCandidate();
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
        checkedNodeIds.Clear();
        excludedNodeIdsForNextRun.Clear();
        maxLevelOverrideForNextRun = null;
        if (!wasManagedByPotsTreasure)
        {
            lastCompletedRunNodeIds.Clear();
        }

        layoutTreasure.Clear();
        pathPlanner = null;
        pandoraAutoOpen.Release();

        if (wasStandalone || wasIllegalFiller)
        {
            modeGuard.NotifyTreasureHuntEnded();
        }
    }
}
