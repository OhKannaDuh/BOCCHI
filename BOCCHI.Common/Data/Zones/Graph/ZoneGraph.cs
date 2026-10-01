using BOCCHI.Common.Services;
using Ocelot.Extensions;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BOCCHI.Common.Data.Zones.Graph;

public class ZoneGraph
{
    [JsonInclude] public Dictionary<Guid, Node> Nodes { get; private set; } = new();

    [JsonInclude] public Dictionary<Guid, List<Edge>> Edges { get; private set; } = new();

    public string ToJson()
    {
        JsonSerializerOptions options = new()
        {
            WriteIndented = true,
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
            Converters =
            {
                new NodeMetadataConverter(),
                new Vector3Converter()
            }
        };

        return JsonSerializer.Serialize(this, options);
    }

    public static ZoneGraph? FromJson(string json)
    {
        JsonSerializerOptions options = new()
        {
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
            Converters =
            {
                new Vector3Converter(),
                new NodeMetadataConverter()
            }
        };

        return JsonSerializer.Deserialize<ZoneGraph>(json, options);
    }

    public bool IsUsableForRouting()
    {
        if (Nodes.Count == 0 || Edges.Count == 0)
        {
            return false;
        }

        if (GetBaseCampReturnPositionNode() == null || GetBaseCampAetheryteNode() == null)
        {
            return false;
        }

        if (!GetTeleportNodes().Any())
        {
            return false;
        }

        List<Node> activities = GetActivityNodes().ToList();
        if (activities.Count == 0)
        {
            return false;
        }

        bool hasFiniteWalk = Edges.Values
            .SelectMany(list => list)
            .Any(edge => edge.Type == EdgeType.Walk && float.IsFinite(edge.Cost) && edge.Cost >= 0f);
        if (!hasFiniteWalk)
        {
            return false;
        }

        // Partial caches softlock specific FATEs/CEs — every activity needs an inbound walk.
        return activities.All(activity => GetInboundTeleport(activity) != null);
    }

    public int CountRoutableActivities() =>
        GetActivityNodes().Count(activity => GetInboundTeleport(activity) != null);

    public void ClearCriticalEncounterCombatRadii()
    {
        foreach (Node node in GetActivityNodes())
        {
            if (node.Type != NodeType.CriticalEncounter || node.Metadata is not ActivityNodeMetadata meta)
            {
                continue;
            }

            meta.CombatRadius = 0f;
            meta.StandRadius = 0f;
        }
    }

    public bool CoversZoneActivities(IZone zone)
    {
        if (!IsUsableForRouting())
        {
            return false;
        }

        List<int> expectedIds = zone.GetNormalFateData().Select(a => a.Id)
            .Concat(zone.GetPotFateData().Select(a => a.Id))
            .Concat(zone.GetCriticalEncounterData().Select(a => a.Id))
            .Distinct()
            .ToList();

        if (expectedIds.Count == 0)
        {
            return true;
        }

        Dictionary<int, Node> byActivityId = [];
        foreach (Node node in GetActivityNodes())
        {
            if (node.Metadata is ActivityNodeMetadata { Id: var id })
            {
                byActivityId.TryAdd(id, node);
            }
        }

        foreach (int id in expectedIds)
        {
            if (!byActivityId.TryGetValue(id, out Node? node) || GetInboundTeleport(node) == null)
            {
                return false;
            }
        }

        return true;
    }

    public void AddNode(Node node)
    {
        Nodes[node.Id] = node;
        if (!Edges.ContainsKey(node.Id))
        {
            Edges[node.Id] = [];
        }
    }

    public void AddEdge(Guid from, Guid to, float cost, EdgeType type)
    {
        if (!Nodes.ContainsKey(from) || !Nodes.ContainsKey(to))
        {
            throw new InvalidOperationException("Both nodes must exist before adding an edge.");
        }

        if (!float.IsFinite(cost))
        {
            return;
        }

        Edges[from].Add(new()
        {
            Type = type,
            From = from,
            To = to,
            Cost = cost
        });
    }

    public void AddTwoWayEdge(Guid a, Guid b, float costAB, EdgeType type)
    {
        AddEdge(a, b, costAB, type);
        AddEdge(b, a, costAB, type);
    }

    public Edge? GetEdge(Node from, Node to)
    {
        if (!Edges.TryGetValue(from.Id, out List<Edge>? list))
        {
            return null;
        }

        return list.FirstOrDefault(e => e.To == to.Id);
    }

    public bool TryGetNode(Vector3 position, float maxDistance, out Node node)
    {
        node = null!;

        if (Nodes.Count == 0)
        {
            return false;
        }

        float maxDistSq = maxDistance * maxDistance;
        float bestDistSq = float.MaxValue;
        Node? best = null;

        foreach (Node n in Nodes.Values)
        {
            float distSq = Vector3.DistanceSquared(n.Position, position);
            if (distSq <= maxDistSq && distSq < bestDistSq)
            {
                bestDistSq = distSq;
                best = n;
            }
        }

        if (best == null)
        {
            return false;
        }

        node = best;

        return true;
    }

    public Node? GetInboundTeleport(Node goal) =>
        GetInboundTeleports(goal).FirstOrDefault().Teleport;

    public IReadOnlyList<(Node Teleport, float Cost)> GetUsableInboundTeleports(Node goal)
    {
        IReadOnlyList<(Node Teleport, float Cost)> wired = GetInboundTeleports(goal)
            .Where(entry => IsTeleportUsable(entry.Teleport))
            .ToList();
        if (wired.Count > 0)
        {
            return wired;
        }

        return EstimateUsableInboundTeleports(goal.Position);
    }

    public IReadOnlyList<(Node Teleport, float Cost)> EstimateUsableInboundTeleports(Vector3 goalPosition) =>
        GetUsableTeleportNodes()
            .Select(tp => (Teleport: tp, Cost: tp.Position.Distance2D(goalPosition)))
            .OrderBy(entry => entry.Cost)
            .ToList();

    public IReadOnlyList<(Node Teleport, float Cost)> GetInboundTeleports(Node goal)
    {
        uint? preferredId = goal.Metadata is ActivityNodeMetadata { PreferredAethernetId: { } id } ? id : null;

        List<(Node Teleport, float Cost)> inbound = Edges
            .Where(kvp => Nodes[kvp.Key].IsTeleport())
            .SelectMany(kvp => kvp.Value
                .Where(e => e.To == goal.Id)
                .Select(e => (Teleport: Nodes[kvp.Key], Cost: e.Cost)))
            .Where(entry => !float.IsPositiveInfinity(entry.Cost) && !float.IsNaN(entry.Cost))
            .OrderBy(entry => entry.Cost)
            .ToList();

        if (inbound.Count == 0)
        {
            return inbound;
        }

        if (preferredId is not { } preferred)
        {
            return inbound;
        }

        return inbound
            .OrderBy(entry => entry.Teleport.Metadata is TeleportNodeMetadata tm && tm.AetheryteId == preferred
                ? 0
                : 1)
            .ThenBy(entry => entry.Cost)
            .ToList();
    }

    public IEnumerable<Node> GetNodesByTypes(params NodeType[] types)
    {
        HashSet<NodeType> set = types.ToHashSet();
        return Nodes.Values.Where(n => set.Contains(n.Type));
    }

    public Node? GetBaseCampReturnPositionNode() => GetNodesByTypes(NodeType.BaseCampReturnPosition).FirstOrDefault();

    public Node? GetBaseCampAetheryteNode() => GetNodesByTypes(NodeType.BaseCampAetheryte).FirstOrDefault();

    public IEnumerable<Node> GetTeleportNodes() =>
        GetNodesByTypes(NodeType.BaseCampAetheryte, NodeType.AethernetShard);

    public IEnumerable<Node> GetUsableTeleportNodes() => GetTeleportNodes().Where(IsTeleportUsable);

    private static bool IsTeleportUsable(Node node) =>
        node.Metadata is not TeleportNodeMetadata teleport
        || OccultCrescentHelper.IsAethernetUnlocked(teleport.AetheryteId);

    public IEnumerable<Node> GetActivityNodes() => GetNodesByTypes(NodeType.NormalFate, NodeType.PotFate, NodeType.CriticalEncounter);

    public Node? GetNearestTeleport(Vector3 pos)
    {
        return GetTeleportNodes()
            .OrderBy(n => Vector3.Distance(n.Position, pos))
            .FirstOrDefault();
    }

    public async Task ConnectToBaseCamp(List<Node> nodes, GraphConfig config)
    {
        const float MaxEuclideanDistance2D = 750f;

        Node? returnNode = GetBaseCampReturnPositionNode();
        if (returnNode == null)
        {
            return;
        }

        foreach (Node node in nodes)
        {
            float euclideanDistance2D = returnNode.Position.Distance2D(node.Position);
            if (euclideanDistance2D > MaxEuclideanDistance2D)
            {
                continue;
            }

            float cost = await config.GetWalkingCost(returnNode, node);

            AddEdge(returnNode.Id, node.Id, cost, EdgeType.Walk);
        }
    }

    public async Task ConnectToNearestTeleports(List<Node> nodes, GraphConfig config)
    {
        List<Node> teleports = GetTeleportNodes().ToList();

        foreach (Node node in nodes)
        {
            List<Node> candidateTeleports = teleports;
            if (node.Metadata is ActivityNodeMetadata { PreferredAethernetId: { } preferredId })
            {
                List<Node> preferred = teleports
                    .Where(t => t.Metadata is TeleportNodeMetadata tm && tm.AetheryteId == preferredId)
                    .ToList();
                if (preferred.Count > 0)
                {
                    candidateTeleports = preferred;
                }
            }

            // Score nearest shards sequentially — parallel WhenAll flooded vnav and stalled movement.
            List<Node> nearestTeleports = NearestTeleports(candidateTeleports, node, takeAll: candidateTeleports != teleports);
            List<(Node Teleport, float InboundCost, float OutboundCost)> scored =
                await ScoreAllTeleportWalks(nearestTeleports, node, config);

            if (scored.Count == 0 && candidateTeleports != teleports)
            {
                scored = await ScoreAllTeleportWalks(NearestTeleports(teleports, node, takeAll: false), node, config);
            }

            foreach ((Node teleport, float inboundCost, float outboundCost) in scored)
            {
                if (!float.IsPositiveInfinity(inboundCost))
                {
                    AddEdge(teleport.Id, node.Id, inboundCost, EdgeType.Walk);
                }

                if (!float.IsPositiveInfinity(outboundCost))
                {
                    AddEdge(node.Id, teleport.Id, outboundCost, EdgeType.Walk);
                }
            }
        }
    }

    private static List<Node> NearestTeleports(List<Node> candidates, Node activity, bool takeAll)
    {
        IOrderedEnumerable<Node> ordered = candidates.OrderBy(t => t.Position.Distance2D(activity.Position));
        return (takeAll ? ordered : ordered.Take(3)).ToList();
    }

    private static async Task<List<(Node Teleport, float InboundCost, float OutboundCost)>> ScoreAllTeleportWalks(
        List<Node> teleports,
        Node activity,
        GraphConfig config)
    {
        List<(Node Teleport, float InboundCost, float OutboundCost)> scored = [];

        foreach (Node teleport in teleports)
        {
            if (teleport.Metadata is not TeleportNodeMetadata meta)
            {
                throw new InvalidOperationException("Teleport node metadata is not set");
            }

            float toActivity = await config.GetWalkingCost(meta.Destination, activity.Position);
            float fromActivity = await config.GetWalkingCost(activity.Position, meta.Destination);
            if (float.IsPositiveInfinity(toActivity) && float.IsPositiveInfinity(fromActivity))
            {
                continue;
            }

            scored.Add((teleport, toActivity, fromActivity));
        }

        return scored;
    }
}
