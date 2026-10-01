using BOCCHI.Common.Data.Zones;
using BOCCHI.Common.Services;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using System.Numerics;
using System.Text.Json;

namespace BOCCHI.Treasure.Hunt;

public interface IHuntRoutePlanner
{
    HuntPathfinderState State { get; }

    IReadOnlyList<string> SegmentIds { get; }

    string? TryGetNodeSegment(uint nodeId);

    int? TryGetNodeOrderIndex(uint nodeId);

    uint? TryGetSegmentFirstNode(string segmentId);

    List<HuntPathfinderStep> BuildEntryLeg(uint toNodeId, bool alreadyInCamp = false);

    Task<List<HuntPathfinderStep>> FindPath(
        Vector3 start,
        List<uint> nodes,
        IReadOnlyList<uint>? preferStartNodes = null,
        uint? continueAfterNodeId = null,
        uint? entryNodeId = null);
}

public abstract class HuntRoutePlanner
(
    ZoneId zoneId,
    IDalamudPluginInterface plugin,
    IPluginLog log
) : IHuntRoutePlanner
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static readonly Dictionary<(ZoneId Zone, string File), HuntNodeDataSchema> NodeDataCache = [];

    private static readonly Dictionary<ZoneId, AuthoredRoutePayload> AuthoredRouteCache = [];

    private readonly record struct AuthoredRoutePayload(
        List<AuthoredRouteEntry> Entries,
        List<AuthoredRouteSegment> Segments);

    private HuntNodeDataSchema data = new();

    private List<AuthoredRouteEntry> authoredEntries = [];

    private List<AuthoredRouteSegment> authoredSegments = [];

    public IReadOnlyList<string> SegmentIds => authoredSegments.Select(seg => seg.Id).ToList();

    private HuntAethernet BaseCampAethernet => zoneId switch
    {
        ZoneId.NorthHorn => HuntAethernet.NorthHornBaseCamp,
        _ => HuntAethernet.BaseCamp,
    };

    public HuntPathfinderState State { get; private set; } = HuntPathfinderState.None;

    public Task<List<HuntPathfinderStep>> FindPath(
        Vector3 start,
        List<uint> nodes,
        IReadOnlyList<uint>? preferStartNodes = null,
        uint? continueAfterNodeId = null,
        uint? entryNodeId = null)
    {
        if (State != HuntPathfinderState.FileLoaded && State != HuntPathfinderState.PathfindingDone)
        {
            throw new InvalidOperationException("Hunt route data not loaded");
        }

        State = HuntPathfinderState.Pathfinding;

        List<uint> remaining = nodes.Distinct().ToList();
        if (remaining.Count == 0)
        {
            State = HuntPathfinderState.PathfindingDone;
            return Task.FromResult(new List<HuntPathfinderStep>());
        }

        List<uint> preferPrefix = [];
        if (preferStartNodes != null)
        {
            HashSet<uint> seen = [];
            foreach (uint id in preferStartNodes)
            {
                if (remaining.Contains(id) && seen.Add(id))
                {
                    preferPrefix.Add(id);
                }
            }
        }

        uint? primaryPrefer = preferPrefix.Count > 0 ? preferPrefix[0] : null;
        List<uint> tour = authoredEntries.Count > 0
            ? BuildAuthoredTour(start, remaining, primaryPrefer, continueAfterNodeId, entryNodeId)
            : BuildTspTour(start, remaining, primaryPrefer);

        if (preferPrefix.Count > 0)
        {
            HashSet<uint> prefixSet = preferPrefix.ToHashSet();
            tour = preferPrefix.Concat(tour.Where(id => !prefixSet.Contains(id))).ToList();
        }

        if (tour.Count == 0)
        {
            State = HuntPathfinderState.PathfindingDone;
            return Task.FromResult(new List<HuntPathfinderStep>());
        }

        log.Debug(
            authoredEntries.Count > 0
                ? "Treasure hunt authored route: {Count} remaining (start {Start}, nearbyPrefix {Prefix}, segment {Segment})"
                : "Treasure hunt nearest-neighbor route: {Count} remaining (start {Start}, nearbyPrefix {Prefix}, segment {Segment})",
            tour.Count,
            tour[0],
            preferPrefix.Count,
            TryGetNodeSegment(tour[0]) ?? "-");

        List<HuntPathfinderStep> steps = BuildStepsForTour(tour);
        State = HuntPathfinderState.PathfindingDone;
        return Task.FromResult(steps);
    }

    public string? TryGetNodeSegment(uint nodeId) =>
        TryGetSegmentIndex(nodeId) is int index ? authoredSegments[index].Id : null;

    public uint? TryGetSegmentFirstNode(string segmentId)
    {
        int index = authoredSegments.FindIndex(seg =>
            string.Equals(seg.Id, segmentId, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            return null;
        }

        foreach (AuthoredRouteEntry entry in authoredEntries)
        {
            if (entry.SegmentIndex == index)
            {
                return entry.NodeId;
            }
        }

        return null;
    }

    private int? TryGetSegmentIndex(uint nodeId)
    {
        foreach (AuthoredRouteEntry entry in authoredEntries)
        {
            if (entry.NodeId == nodeId)
            {
                return entry.SegmentIndex >= 0 ? entry.SegmentIndex : null;
            }
        }

        return null;
    }

    public int? TryGetNodeOrderIndex(uint nodeId)
    {
        for (int i = 0; i < authoredEntries.Count; i++)
        {
            if (authoredEntries[i].NodeId == nodeId)
            {
                return i;
            }
        }

        return null;
    }

    protected abstract Vector3 GetNodePosition(uint nodeId);

    public static void InvalidateCaches()
    {
        NodeDataCache.Clear();
        AuthoredRouteCache.Clear();
    }

    protected void LoadFile(string filename)
    {
        State = HuntPathfinderState.LoadingFile;

        if (NodeDataCache.TryGetValue((zoneId, filename), out HuntNodeDataSchema? cached))
        {
            data = cached;
            if (AuthoredRouteCache.TryGetValue(zoneId, out AuthoredRoutePayload route))
            {
                authoredEntries = route.Entries;
                authoredSegments = route.Segments;
            }
            else
            {
                authoredEntries = [];
                authoredSegments = [];
            }

            State = HuntPathfinderState.FileLoaded;
            return;
        }

        string file = GetDataFile(plugin, zoneId, filename);
        if (!File.Exists(file))
        {
            log.Error($"Required hunt data file not found: {file}");
            return;
        }

        string json = File.ReadAllText(file);
        data = JsonSerializer.Deserialize<HuntNodeDataSchema>(json) ?? new HuntNodeDataSchema();
        LoadAuthoredRoute();

        NodeDataCache[(zoneId, filename)] = data;
        AuthoredRouteCache[zoneId] = new AuthoredRoutePayload(authoredEntries, authoredSegments);
        log.Debug(
            "Cached hunt route data for {Zone}: {Nodes} node(s), {Pads} authored location(s)",
            zoneId,
            data.NodeToNodeDistances.Count,
            authoredEntries.Count);

        State = HuntPathfinderState.FileLoaded;
    }

    private void LoadAuthoredRoute()
    {
        authoredEntries = [];
        authoredSegments = [];
        string file = GetDataFile(plugin, zoneId, "treasure_route.json");
        if (!File.Exists(file))
        {
            log.Debug("No treasure_route.json for {Zone}; using nearest-neighbor TSP", zoneId);
            return;
        }

        try
        {
            string json = File.ReadAllText(file);
            AuthoredTreasureRoute? route = JsonSerializer.Deserialize<AuthoredTreasureRoute>(json, JsonOptions);
            if (route is not { SchemaVersion: >= 2 } || route.Segments.Count == 0)
            {
                log.Debug(
                    "treasure_route.json for {Zone} is not schema v2 with segments; using TSP",
                    zoneId);
                return;
            }

            foreach (AuthoredTreasureSegment segment in route.Segments)
            {
                if (segment.Nodes.Count == 0)
                {
                    continue;
                }

                int segmentIndex = authoredSegments.Count;
                authoredSegments.Add(new AuthoredRouteSegment(segment.Id, segment.TransitionAfter));
                foreach (uint nodeId in segment.Nodes)
                {
                    authoredEntries.Add(new AuthoredRouteEntry(nodeId, segmentIndex));
                }
            }

            log.Debug(
                "Loaded authored treasure route for {Zone}: {Pads} locations in {Segments} segment(s)",
                zoneId,
                authoredEntries.Count,
                route.Segments.Count);
        }
        catch (Exception ex)
        {
            authoredEntries = [];
            authoredSegments = [];
            log.Warning(ex, "Failed to load treasure_route.json for {Zone}; using TSP", zoneId);
        }
    }

    private List<uint> BuildTspTour(Vector3 start, List<uint> remaining, uint? preferStartNode)
    {
        uint startNode = preferStartNode is uint preferred && remaining.Contains(preferred)
            ? preferred
            : remaining
                .OrderBy(id => Vector3.DistanceSquared(start, GetNodePosition(id)))
                .First();

        Dictionary<uint, Dictionary<uint, (float Cost, List<HuntPathfinderStep> Steps)>> graph =
            BuildCostGraph(remaining);
        List<uint> route = SolveTspNearestNeighbor(startNode, remaining, graph);
        return ImproveWithTwoOpt(route, graph);
    }

    private static List<uint> ImproveWithTwoOpt(
        List<uint> route,
        Dictionary<uint, Dictionary<uint, (float Cost, List<HuntPathfinderStep> Steps)>> graph)
    {
        const int maxPasses = 40;
        const float minGain = 0.01f;

        if (route.Count < 4)
        {
            return route;
        }

        for (int pass = 0; pass < maxPasses; pass++)
        {
            bool improved = false;

            for (int i = 0; i < route.Count - 2; i++)
            {
                for (int k = i + 2; k < route.Count; k++)
                {
                    float before = EdgeCost(graph, route[i], route[i + 1]);
                    float after = EdgeCost(graph, route[i], route[k]);

                    if (k + 1 < route.Count)
                    {
                        before += EdgeCost(graph, route[k], route[k + 1]);
                        after += EdgeCost(graph, route[i + 1], route[k + 1]);
                    }

                    if (after + minGain >= before)
                    {
                        continue;
                    }

                    route.Reverse(i + 1, k - i);
                    improved = true;
                }
            }

            if (!improved)
            {
                break;
            }
        }

        return route;
    }

    private static float EdgeCost(
        Dictionary<uint, Dictionary<uint, (float Cost, List<HuntPathfinderStep> Steps)>> graph,
        uint from,
        uint to)
    {
        if (graph.TryGetValue(from, out Dictionary<uint, (float Cost, List<HuntPathfinderStep> Steps)>? edges)
            && edges.TryGetValue(to, out (float Cost, List<HuntPathfinderStep> Steps) edge))
        {
            return edge.Cost;
        }

        return 1e9f;
    }

    private List<uint> BuildAuthoredTour(
        Vector3 start,
        List<uint> remaining,
        uint? preferStartNode,
        uint? continueAfterNodeId = null,
        uint? entryNodeId = null)
    {
        HashSet<uint> remainingSet = remaining.ToHashSet();
        Dictionary<uint, int> orderIndex = [];
        List<AuthoredRouteEntry> orderedUnique = [];
        foreach (AuthoredRouteEntry entry in authoredEntries)
        {
            if (!remainingSet.Contains(entry.NodeId) || orderIndex.ContainsKey(entry.NodeId))
            {
                continue;
            }

            orderIndex[entry.NodeId] = orderedUnique.Count;
            orderedUnique.Add(entry);
        }

        foreach (uint id in remaining.Where(id => !orderIndex.ContainsKey(id)))
        {
            orderIndex[id] = orderedUnique.Count;
            orderedUnique.Add(new AuthoredRouteEntry(id, -1));
        }

        if (orderedUnique.Count == 0)
        {
            return [];
        }

        List<uint> tour = BuildOrderedTour(start, orderedUnique, continueAfterNodeId, entryNodeId);
        if (preferStartNode is uint prefer && orderIndex.ContainsKey(prefer))
        {
            tour.Remove(prefer);
            tour.Insert(0, prefer);
        }

        return tour;
    }

    private List<uint> BuildOrderedTour(
        Vector3 start,
        List<AuthoredRouteEntry> orderedUnique,
        uint? continueAfterNodeId,
        uint? entryNodeId)
    {
        HashSet<uint> remainingIds = orderedUnique.Select(e => e.NodeId).ToHashSet();

        uint? entry = entryNodeId is uint rotation && remainingIds.Contains(rotation)
            ? rotation
            : null;

        entry ??= continueAfterNodeId is uint after
            ? TryGetNextAuthoredAfter(after, orderedUnique)
            : null;

        entry ??= orderedUnique
            .OrderBy(e => Vector3.DistanceSquared(start, GetNodePosition(e.NodeId)))
            .First()
            .NodeId;

        return OrderFromEntry(orderedUnique, entry.Value);
    }

    private uint? TryGetNextAuthoredAfter(uint afterNodeId, List<AuthoredRouteEntry> remaining)
    {
        int start = -1;
        for (int i = 0; i < authoredEntries.Count; i++)
        {
            if (authoredEntries[i].NodeId == afterNodeId)
            {
                start = i;
                break;
            }
        }

        if (start < 0)
        {
            return null;
        }

        HashSet<uint> remainingIds = remaining.Select(e => e.NodeId).ToHashSet();
        for (int step = 1; step <= authoredEntries.Count; step++)
        {
            uint id = authoredEntries[(start + step) % authoredEntries.Count].NodeId;
            if (remainingIds.Contains(id))
            {
                return id;
            }
        }

        return null;
    }

    private static List<uint> OrderFromEntry(List<AuthoredRouteEntry> orderedUnique, uint entry)
    {
        int idx = orderedUnique.FindIndex(e => e.NodeId == entry);
        if (idx < 0)
        {
            return orderedUnique.Select(e => e.NodeId).ToList();
        }

        List<uint> tour = [];
        for (int i = 0; i < orderedUnique.Count; i++)
        {
            tour.Add(orderedUnique[(idx + i) % orderedUnique.Count].NodeId);
        }

        return tour;
    }

    private List<HuntPathfinderStep> BuildStepsForTour(List<uint> tour)
    {
        List<HuntPathfinderStep> steps = [HuntPathfinderStep.WalkToDestination(tour[0])];
        for (int i = 0; i < tour.Count - 1; i++)
        {
            uint from = tour[i];
            uint to = tour[i + 1];
            AuthoredTreasureTransition? transition = FindTransitionBetween(from, to);
            steps.AddRange(ResolveLeg(from, to, transition));
        }

        return steps;
    }

    private AuthoredTreasureTransition? FindTransitionBetween(uint from, uint to)
    {
        int? fromSegment = TryGetSegmentIndex(from);
        int? toSegment = TryGetSegmentIndex(to);

        if (fromSegment != null && fromSegment == toSegment)
        {
            return new AuthoredTreasureTransition { Type = "walk" };
        }

        if (fromSegment is not int index)
        {
            return new AuthoredTreasureTransition { Type = "auto" };
        }

        return authoredSegments[index].TransitionAfter
               ?? new AuthoredTreasureTransition { Type = "auto" };
    }

    private List<HuntPathfinderStep> ResolveLeg(uint fromId, uint toId, AuthoredTreasureTransition? transition)
    {
        string type = transition?.Type?.Trim().ToLowerInvariant() ?? "walk";
        switch (type)
        {
            case "return":
                return BuildEntryLeg(toId);
            case "teleport" when TryParseAethernet(transition?.To, out HuntAethernet shard):
                if (!IsUsableHuntDestination(shard))
                {
                    return GetBestSteps(fromId, toId).Steps;
                }

                return
                [
                    HuntPathfinderStep.ReturnToBaseCamp(),
                    HuntPathfinderStep.TeleportToAethernet(shard),
                    HuntPathfinderStep.WalkToDestination(toId)
                ];
            case "none":
            case "walk":
                return [HuntPathfinderStep.WalkToDestination(toId)];
            default:
                return GetBestSteps(fromId, toId).Steps;
        }
    }

    public List<HuntPathfinderStep> BuildEntryLeg(uint toId, bool alreadyInCamp = false)
    {
        HuntAethernet baseCamp = BaseCampAethernet;

        float bestCost = float.MaxValue;
        if (data.AethernetToNodeDistances.TryGetValue(baseCamp, out List<HuntToNode>? fromBase))
        {
            HuntToNode walkFromCamp = fromBase.FirstOrDefault(x => x.Id == toId);
            if (walkFromCamp.Id == toId)
            {
                bestCost = walkFromCamp.Distance;
            }
        }

        HuntAethernet? bestShard = null;
        foreach ((HuntAethernet aethernet, List<HuntToNode> list) in data.AethernetToNodeDistances)
        {
            if (aethernet == baseCamp || !IsUsableHuntDestination(aethernet))
            {
                continue;
            }

            HuntToNode to = list.FirstOrDefault(x => x.Id == toId);
            if (to.Id != toId)
            {
                continue;
            }

            float cost = NavigationConstants.AethernetHopCost + to.Distance;
            if (cost >= bestCost)
            {
                continue;
            }

            bestCost = cost;
            bestShard = aethernet;
        }

        List<HuntPathfinderStep> steps = [];
        if (!alreadyInCamp)
        {
            steps.Add(HuntPathfinderStep.ReturnToBaseCamp());
        }

        if (bestShard is HuntAethernet shard)
        {
            steps.Add(HuntPathfinderStep.TeleportToAethernet(shard));
        }

        steps.Add(HuntPathfinderStep.WalkToDestination(toId));
        return steps;
    }

    private static bool TryParseAethernet(string? name, out HuntAethernet aethernet)
    {
        aethernet = default;
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        return Enum.TryParse(name.Trim(), ignoreCase: true, out aethernet);
    }

    private bool IsUsableHuntDestination(HuntAethernet aethernet) =>
        aethernet == BaseCampAethernet
        || OccultCrescentHelper.IsAethernetUnlocked((uint)aethernet);

    protected (float Cost, List<HuntPathfinderStep> Steps) GetBestSteps(uint fromId, uint toId)
    {
        float bestCost = float.MaxValue;
        List<HuntPathfinderStep> bestSteps = [];

        if (data.NodeToNodeDistances.TryGetValue(fromId, out List<HuntToNode>? directList))
        {
            HuntToNode direct = directList.FirstOrDefault(x => x.Id == toId);
            if (direct.Id == toId)
            {
                bestCost = direct.Distance;
                bestSteps = [HuntPathfinderStep.WalkToDestination(toId)];
            }
        }

        if (data.NodeToAethernetDistances.TryGetValue(fromId, out List<HuntToAethernet>? shardList) && shardList.Count > 0)
        {
            HuntToAethernet fromShard = shardList.OrderBy(x => x.Distance).First();
            foreach ((HuntAethernet aethernet, List<HuntToNode> list) in data.AethernetToNodeDistances)
            {
                if (!IsUsableHuntDestination(aethernet))
                {
                    continue;
                }

                HuntToNode to = list.FirstOrDefault(x => x.Id == toId);
                if (to.Id != toId)
                {
                    continue;
                }

                float cost = fromShard.Distance + NavigationConstants.AethernetHopCost + to.Distance;
                if (cost < bestCost)
                {
                    bestCost = cost;
                    bestSteps =
                    [
                        HuntPathfinderStep.WalkToAethernet(fromShard.Aethernet),
                        HuntPathfinderStep.TeleportToAethernet(aethernet),
                        HuntPathfinderStep.WalkToDestination(toId)
                    ];
                }
            }
        }

        HuntAethernet baseCamp = BaseCampAethernet;
        if (data.AethernetToNodeDistances.TryGetValue(baseCamp, out List<HuntToNode>? fromBaseList))
        {
            HuntToNode walkFromBase = fromBaseList.FirstOrDefault(x => x.Id == toId);
            if (walkFromBase.Id == toId)
            {
                float returnWalkCost = NavigationConstants.ReturnCost + walkFromBase.Distance;
                if (returnWalkCost < bestCost)
                {
                    bestCost = returnWalkCost;
                    bestSteps =
                    [
                        HuntPathfinderStep.ReturnToBaseCamp(),
                        HuntPathfinderStep.WalkToDestination(toId)
                    ];
                }
            }
        }

        foreach ((HuntAethernet aethernet, List<HuntToNode> list) in data.AethernetToNodeDistances)
        {
            if (aethernet == baseCamp || !IsUsableHuntDestination(aethernet))
            {
                continue;
            }

            HuntToNode to = list.FirstOrDefault(x => x.Id == toId);
            if (to.Id != toId)
            {
                continue;
            }

            float cost = NavigationConstants.ReturnCost + NavigationConstants.AethernetHopCost + to.Distance;
            if (cost < bestCost)
            {
                bestCost = cost;
                bestSteps =
                [
                    HuntPathfinderStep.ReturnToBaseCamp(),
                    HuntPathfinderStep.TeleportToAethernet(aethernet),
                    HuntPathfinderStep.WalkToDestination(toId)
                ];
            }
        }

        if (bestSteps.Count == 0)
        {
            bestCost = Vector3.Distance(GetNodePosition(fromId), GetNodePosition(toId));
            bestSteps = [HuntPathfinderStep.WalkToDestination(toId)];
        }

        return (bestCost, bestSteps);
    }

    private Dictionary<uint, Dictionary<uint, (float Cost, List<HuntPathfinderStep> Steps)>> BuildCostGraph(List<uint> nodes)
    {
        Dictionary<uint, Dictionary<uint, (float, List<HuntPathfinderStep>)>> graph = new();

        foreach (uint from in nodes)
        {
            graph[from] = new();
            foreach (uint to in nodes)
            {
                if (from == to)
                {
                    continue;
                }

                graph[from][to] = GetBestSteps(from, to);
            }
        }

        return graph;
    }

    private static List<uint> SolveTspNearestNeighbor(
        uint start,
        List<uint> nodes,
        Dictionary<uint, Dictionary<uint, (float Cost, List<HuntPathfinderStep> Steps)>> graph
    )
    {
        if (nodes.Count == 0)
        {
            return [];
        }

        if (nodes.Count == 1)
        {
            return [start];
        }

        List<uint> route = [start];
        HashSet<uint> unvisited = new(nodes.Where(n => n != start));

        while (unvisited.Count > 0)
        {
            uint last = route[^1];
            uint? nearest = null;
            float minCost = float.MaxValue;

            foreach (uint candidate in unvisited)
            {
                float cost = graph[last][candidate].Cost;
                if (cost < minCost)
                {
                    minCost = cost;
                    nearest = candidate;
                }
            }

            if (nearest is not uint next)
            {
                break;
            }

            route.Add(next);
            unvisited.Remove(next);
        }

        return route;
    }

    private static string GetDataFile(IDalamudPluginInterface plugin, ZoneId zoneId, string filename)
    {
        string pluginDir = GetPluginDirectory(plugin);
        return Path.Combine(pluginDir, "Data", zoneId.TreasureDataFolder(), filename);
    }

    private static string GetPluginDirectory(IDalamudPluginInterface plugin)
    {
        string? pluginDir = plugin.AssemblyLocation.DirectoryName;
        if (!string.IsNullOrEmpty(pluginDir))
        {
            return pluginDir;
        }

        string? assemblyDir = Path.GetDirectoryName(plugin.GetType().Assembly.Location);
        if (!string.IsNullOrEmpty(assemblyDir))
        {
            return assemblyDir;
        }

        throw new InvalidOperationException("Unable to resolve the BOCCHI plugin directory for hunt data files.");
    }
}
