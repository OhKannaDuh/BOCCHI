using BOCCHI.Common.Data.Zones;
using BOCCHI.Common.Data.Zones.Graph;
using BOCCHI.Treasure.Hunt;
using BOCCHI.Treasure.Services;
using Dalamud.Bindings.ImGui;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.LayoutEngine;
using FFXIVClientStructs.Interop;
using FFXIVClientStructs.STD;
using Ocelot.Services.UI;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BOCCHI.Debug.Panels;

/// <summary>Exports bronze/silver layout pads to <c>treasure_locations.json</c>.</summary>
public sealed class TreasureLocationsExportPanel
(
    IZoneProvider zones,
    IDataManager data,
    IDalamudPluginInterface plugin,
    IPluginLog log,
    IBrandingService branding,
    IUIService ui
) : IDebugPanel
{
    private const string Filename = "treasure_locations.json";

    /// <summary>Export filename — not treasure_route.json (overwriting that dropped South Horn to NN).</summary>
    private const string RouteFilename = "treasure_route.suggested.json";

    private static readonly JsonSerializerOptions WriteJson = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly List<LayoutTreasure> treasures = [];

    private string? lastError;

    private string? lastOutputPath;

    private string? lastRefreshSummary;

    public string Name => "Treasure Locations Export";

    public void Render()
    {
        IZone zone = zones.GetZone();
        if (!zone.IsOccultCrescentZone())
        {
            ui.Text("Enter South Horn or North Horn first.");
            return;
        }

        List<TreasureData> authored = zone.GetTreasureData();

        ui.Text(
            "Reads bronze/silver pads from the game layout, then writes treasure_locations.json.",
            branding.DalamudGrey);
        ui.Text(
            "Write route suggestion builds a nearest-neighbor tour from base camp (2D; layout if refreshed, else authored). "
            + "Writes treasure_route.suggested.json — merge into treasure_route.json by hand.",
            branding.DalamudGrey);
        ui.Text("No Worker — layout already has exact coffer positions.", branding.DalamudGrey);

        if (lastError != null)
        {
            ui.Text(lastError, branding.DalamudRed);
        }

        if (lastRefreshSummary != null)
        {
            ui.Text(lastRefreshSummary, branding.DalamudYellow);
        }

        ui.LabelledValue("Layout spots", treasures.Count);
        ui.LabelledValue("Bronze", treasures.Count(t => t.SgbId == TreasureCoffer.BronzeSgbId));
        ui.LabelledValue("Silver", treasures.Count(t => t.SgbId == TreasureCoffer.SilverSgbId));
        ui.LabelledValue("Authored", authored.Count);

        if (treasures.Count > 0 && authored.Count > 0)
        {
            int matched = treasures.Count(t => FindAuthoredLevel(t, authored) != null);
            ui.LabelledValue("Level matched", matched);
        }

        if (ImGui.Button("Refresh from layout"))
        {
            try
            {
                RefreshFromLayout(zone);
                lastError = null;
                lastRefreshSummary =
                    $"Loaded {treasures.Count} layout coffers ({treasures.Count(t => t.SgbId == TreasureCoffer.BronzeSgbId)} bronze / {treasures.Count(t => t.SgbId == TreasureCoffer.SilverSgbId)} silver).";
            }
            catch (Exception ex)
            {
                lastError = ex.Message;
                lastRefreshSummary = null;
                log.Error(ex, "[TreasureLocationsExport] Refresh failed");
            }
        }

        if (lastOutputPath != null)
        {
            ui.Text($"Wrote {lastOutputPath}", branding.DalamudYellow);
        }

        if (treasures.Count > 0 && ImGui.Button("Write treasure_locations.json"))
        {
            try
            {
                List<string> written = WriteLocationsJson(zone, treasures, authored);
                lastOutputPath = string.Join(" | ", written);
                lastError = null;
                log.Information("[TreasureLocationsExport] Wrote locations {Paths}", lastOutputPath);
            }
            catch (Exception ex)
            {
                lastError = ex.Message;
                log.Error(ex, "[TreasureLocationsExport] Write locations failed");
            }
        }

        if (ImGui.Button("Write treasure_route.suggested.json"))
        {
            try
            {
                List<RoutePad> pads = BuildRoutePads(zone, treasures, authored);
                if (pads.Count == 0)
                {
                    throw new InvalidOperationException("No pads with positions to route.");
                }

                List<string> written = WriteRouteJson(zone, pads);
                lastOutputPath = string.Join(" | ", written);
                lastError = null;
                lastRefreshSummary = $"Route: {pads.Count} pads (nearest-neighbor from base camp, 2D).";
                log.Information("[TreasureLocationsExport] Wrote route {Paths}", lastOutputPath);
            }
            catch (Exception ex)
            {
                lastError = ex.Message;
                log.Error(ex, "[TreasureLocationsExport] Write route failed");
            }
        }
    }

    private unsafe void RefreshFromLayout(IZone zone)
    {
        LayoutManager* layout = LayoutWorld.Instance()->ActiveLayout;
        if (layout == null)
        {
            throw new InvalidOperationException("No active layout.");
        }

        if (!layout->InstancesByType.TryGetValue(
                InstanceType.Treasure,
                out Pointer<StdMap<ulong, Pointer<ILayoutInstance>>> _,
                false))
        {
            throw new InvalidOperationException("No treasure layout instances.");
        }

        treasures.Clear();
        foreach (LayoutTreasureScan.Spot spot in LayoutTreasureScan.CollectBronzeSilver(zone, data))
        {
            treasures.Add(new LayoutTreasure(spot.DataId, spot.Position, spot.SgbId));
        }
    }

    private List<string> WriteLocationsJson(
        IZone zone,
        List<LayoutTreasure> spots,
        List<TreasureData> authored)
    {
        string zoneFolder = zone.ZoneId.TreasureDataFolder();

        List<TreasureJsonEntry> entries = [];
        int id = 1;
        foreach (LayoutTreasure spot in spots)
        {
            entries.Add(new TreasureJsonEntry
            {
                Id = id++,
                DataId = spot.DataId,
                X = spot.Position.X,
                Y = spot.Position.Y,
                Z = spot.Position.Z,
                SgbId = spot.SgbId,
                Level = FindAuthoredLevel(spot, authored),
            });
        }

        var payload = new TreasureLocationsFile
        {
            SchemaVersion = 1,
            TerritoryId = zone.TerritoryType,
            Zone = zoneFolder,
            GeneratedAtUtc = DateTime.UtcNow.ToString("O"),
            Source = "layout",
            Treasures = entries,
        };

        string json = JsonSerializer.Serialize(payload, WriteJson);
        return TreasureDataPaths.WriteZoneDataFile(
            plugin.AssemblyLocation.DirectoryName,
            zoneFolder,
            Filename,
            json);
    }

    private List<string> WriteRouteJson(IZone zone, List<RoutePad> pads)
    {
        string zoneFolder = zone.ZoneId.TreasureDataFolder();
        Vector3 camp = zone.GetMainAetheryte().Position;
        List<RoutePad> ordered = BuildNearestNeighborTour(camp, pads);

        List<RouteJsonEntry> route = [];
        for (int i = 0; i < ordered.Count; i++)
        {
            RoutePad pad = ordered[i];
            route.Add(new RouteJsonEntry
            {
                Order = i + 1,
                DataId = pad.DataId,
                X = pad.Position.X,
                Y = pad.Position.Y,
                Z = pad.Position.Z,
                SgbId = pad.SgbId,
                Level = pad.Level,
            });
        }

        var payload = new TreasureRouteFile
        {
            SchemaVersion = 1,
            TerritoryId = zone.TerritoryType,
            Zone = zoneFolder,
            GeneratedAtUtc = DateTime.UtcNow.ToString("O"),
            Source = pads.Any(p => p.SgbId != null) ? "layout-nn-from-camp" : "authored-nn-from-camp",
            Start = new RouteStartJson
            {
                Label = "BaseCamp",
                X = camp.X,
                Y = camp.Y,
                Z = camp.Z,
            },
            Route = route,
        };

        string json = JsonSerializer.Serialize(payload, WriteJson);
        return TreasureDataPaths.WriteZoneDataFile(
            plugin.AssemblyLocation.DirectoryName,
            zoneFolder,
            RouteFilename,
            json);
    }

    private static List<RoutePad> BuildRoutePads(
        IZone zone,
        List<LayoutTreasure> layout,
        List<TreasureData> authored)
    {
        if (layout.Count > 0)
        {
            return layout.Select(t => new RoutePad(
                t.DataId,
                t.Position,
                t.SgbId,
                FindAuthoredLevel(t, authored))).ToList();
        }

        return authored
            .Where(a => a.Position is { })
            .Select(a => new RoutePad((uint)a.Id, a.Position!.Value, null, a.Level))
            .ToList();
    }

    private static List<RoutePad> BuildNearestNeighborTour(Vector3 start, List<RoutePad> pads)
    {
        List<RoutePad> remaining = [.. pads];
        List<RoutePad> tour = [];
        Vector3 current = start;

        while (remaining.Count > 0)
        {
            int bestIndex = 0;
            float bestDist = float.MaxValue;
            for (int i = 0; i < remaining.Count; i++)
            {
                float d = Vector3.DistanceSquared(
                    new Vector3(current.X, 0f, current.Z),
                    new Vector3(remaining[i].Position.X, 0f, remaining[i].Position.Z));
                if (d < bestDist)
                {
                    bestDist = d;
                    bestIndex = i;
                }
            }

            RoutePad next = remaining[bestIndex];
            remaining.RemoveAt(bestIndex);
            tour.Add(next);
            current = next.Position;
        }

        return tour;
    }

    private static int? FindAuthoredLevel(LayoutTreasure spot, List<TreasureData> authored)
    {
        TreasureData? byId = authored.FirstOrDefault(a => a.Id == spot.DataId);
        if (byId != null)
        {
            return byId.Level;
        }

        TreasureData? nearest = null;
        float bestDistSq = HuntDistances.LayoutProximityRadiusSq;
        foreach (TreasureData entry in authored)
        {
            if (entry.Position is not { } pos)
            {
                continue;
            }

            float distSq = Vector3.DistanceSquared(pos, spot.Position);
            if (distSq > bestDistSq)
            {
                continue;
            }

            bestDistSq = distSq;
            nearest = entry;
        }

        return nearest?.Level;
    }

    private readonly record struct LayoutTreasure(uint DataId, Vector3 Position, uint SgbId);

    private readonly record struct RoutePad(uint DataId, Vector3 Position, uint? SgbId, int? Level);

    private sealed class TreasureLocationsFile
    {
        [JsonPropertyName("schemaVersion")]
        public int SchemaVersion { get; set; }

        [JsonPropertyName("territoryId")]
        public int TerritoryId { get; set; }

        [JsonPropertyName("zone")]
        public string Zone { get; set; } = "";

        [JsonPropertyName("generatedAtUtc")]
        public string GeneratedAtUtc { get; set; } = "";

        [JsonPropertyName("source")]
        public string Source { get; set; } = "";

        [JsonPropertyName("treasures")]
        public List<TreasureJsonEntry> Treasures { get; set; } = [];
    }

    private sealed class TreasureJsonEntry
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("dataId")]
        public uint DataId { get; set; }

        [JsonPropertyName("x")]
        public float X { get; set; }

        [JsonPropertyName("y")]
        public float Y { get; set; }

        [JsonPropertyName("z")]
        public float Z { get; set; }

        [JsonPropertyName("sgbId")]
        public uint SgbId { get; set; }

        [JsonPropertyName("level")]
        public int? Level { get; set; }
    }

    private sealed class TreasureRouteFile
    {
        [JsonPropertyName("schemaVersion")]
        public int SchemaVersion { get; set; }

        [JsonPropertyName("territoryId")]
        public int TerritoryId { get; set; }

        [JsonPropertyName("zone")]
        public string Zone { get; set; } = "";

        [JsonPropertyName("generatedAtUtc")]
        public string GeneratedAtUtc { get; set; } = "";

        [JsonPropertyName("source")]
        public string Source { get; set; } = "";

        [JsonPropertyName("start")]
        public RouteStartJson Start { get; set; } = new();

        [JsonPropertyName("route")]
        public List<RouteJsonEntry> Route { get; set; } = [];
    }

    private sealed class RouteStartJson
    {
        [JsonPropertyName("label")]
        public string Label { get; set; } = "";

        [JsonPropertyName("x")]
        public float X { get; set; }

        [JsonPropertyName("y")]
        public float Y { get; set; }

        [JsonPropertyName("z")]
        public float Z { get; set; }
    }

    private sealed class RouteJsonEntry
    {
        [JsonPropertyName("order")]
        public int Order { get; set; }

        [JsonPropertyName("dataId")]
        public uint DataId { get; set; }

        [JsonPropertyName("x")]
        public float X { get; set; }

        [JsonPropertyName("y")]
        public float Y { get; set; }

        [JsonPropertyName("z")]
        public float Z { get; set; }

        [JsonPropertyName("sgbId")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public uint? SgbId { get; set; }

        [JsonPropertyName("level")]
        public int? Level { get; set; }
    }
}
