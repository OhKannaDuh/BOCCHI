using System.Numerics;
using BOCCHI.Common.Data.Zones;
using BOCCHI.Common.Data.Zones.Graph;
using BOCCHI.Common.Data.Zones.Graph.Factory;
using BOCCHI.Common.Data.Zones.Implementations.NorthHorn;
using BOCCHI.Common.Data.Zones.Implementations.SouthHorn;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Ocelot.Services.Logger;
using Ocelot.Services.Pathfinding;
using Xunit;

namespace BOCCHI.Tests;

public class ZoneRouteTests
{
    [Fact]
    public void GraphRoundTripPreservesNodesEdgesAndMetadata()
    {
        ZoneGraph graph = new();
        Node activity = new()
        {
            Type = NodeType.NormalFate,
            Position = new Vector3(1, 2, 3),
            Metadata = new ActivityNodeMetadata
            {
                Id = 42, PreferredAethernetId = 5571, CombatRadius = 30,
                StandRadius = 12, AreaShape = ActivityAreaShape.Square
            }
        };
        Node shard = new()
        {
            Type = NodeType.AethernetShard,
            Position = new Vector3(4, 5, 6),
            Metadata = new TeleportNodeMetadata
            {
                AetheryteId = 5571, Destination = new Vector3(7, 8, 9), DeadRadius = 4
            }
        };
        graph.AddNode(activity);
        graph.AddNode(shard);
        graph.AddEdge(shard.Id, activity.Id, 123.5f, EdgeType.Walk);
        graph.AddEdge(activity.Id, shard.Id, 7, EdgeType.Teleport);

        ZoneGraph restored = Assert.IsType<ZoneGraph>(ZoneGraph.FromJson(graph.ToJson()));

        Assert.Equal(2, restored.Nodes.Count);
        Assert.Equal(2, restored.Edges.Count);
        foreach (Node original in graph.Nodes.Values)
        {
            Node node = restored.Nodes[original.Id];
            Assert.Equal(original.Id, node.Id);
            Assert.Equal(original.Type, node.Type);
            Assert.Equal(original.Position, node.Position);
            Edge edge = Assert.Single(restored.Edges[original.Id]);
            Edge expected = Assert.Single(graph.Edges[original.Id]);
            Assert.Equal(expected.From, edge.From);
            Assert.Equal(expected.To, edge.To);
            Assert.Equal(expected.Type, edge.Type);
            Assert.Equal(expected.Cost, edge.Cost);
        }
        ActivityNodeMetadata activityMetadata = Assert.IsType<ActivityNodeMetadata>(restored.Nodes[activity.Id].Metadata);
        Assert.Equal(42, activityMetadata.Id);
        Assert.Equal(5571u, activityMetadata.PreferredAethernetId);
        Assert.Equal(30f, activityMetadata.CombatRadius);
        Assert.Equal(12f, activityMetadata.StandRadius);
        Assert.Equal(ActivityAreaShape.Square, activityMetadata.AreaShape);
        TeleportNodeMetadata teleportMetadata = Assert.IsType<TeleportNodeMetadata>(restored.Nodes[shard.Id].Metadata);
        Assert.Equal(5571u, teleportMetadata.AetheryteId);
        Assert.Equal(new Vector3(7, 8, 9), teleportMetadata.Destination);
        Assert.Equal(4f, teleportMetadata.DeadRadius);
    }

    [Fact]
    public void PreferredShardWinsOverCheaperInboundWalk()
    {
        ZoneGraph graph = new();
        Node activity = new() { Type = NodeType.NormalFate, Metadata = new ActivityNodeMetadata { PreferredAethernetId = 2 } };
        Node cheap = new() { Type = NodeType.AethernetShard, Metadata = new TeleportNodeMetadata { AetheryteId = 1 } };
        Node preferred = new() { Type = NodeType.AethernetShard, Metadata = new TeleportNodeMetadata { AetheryteId = 2 } };
        graph.AddNode(activity);
        graph.AddNode(cheap);
        graph.AddNode(preferred);
        graph.AddEdge(cheap.Id, activity.Id, 1, EdgeType.Walk);
        graph.AddEdge(preferred.Id, activity.Id, 100, EdgeType.Walk);

        Assert.Same(preferred, graph.GetInboundTeleport(activity));
        Assert.Equal(new[] { preferred, cheap }, graph.GetInboundTeleports(activity).Select(entry => entry.Teleport));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AuthoredTablesHaveUniqueIdsWithinEachList(bool north)
    {
        IObjectTable objects = StandIn.For<IObjectTable>();
        IDalamudPluginInterface plugin = StandIn.For<IDalamudPluginInterface>();
        IGraphFactory graphs = StandIn.For<IGraphFactory>();
        IPathfinder pathfinder = StandIn.For<IPathfinder>();
        ILogger logger = StandIn.For<ILogger>();
        BaseZone zone = north
            ? new NorthHorn(objects, plugin, graphs, pathfinder, logger)
            : new SouthHorn(objects, plugin, graphs, pathfinder, logger);

        AssertUnique(zone.GetNormalFateData().Select(entry => entry.Id));
        AssertUnique(zone.GetPotFateData().Select(entry => entry.Id));
        AssertUnique(zone.GetCriticalEncounterData().Select(entry => entry.Id));
        AssertUnique(zone.GetTreasureData().Select(entry => entry.Id));
        AssertUnique(zone.GetCarrotData().Select(entry => entry.Id));
    }

    private static void AssertUnique(IEnumerable<int> ids)
    {
        List<int> entries = ids.ToList();
        Assert.NotEmpty(entries);
        Assert.Equal(entries.Count, entries.Distinct().Count());
    }
}
