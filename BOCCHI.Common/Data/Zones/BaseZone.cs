using BOCCHI.Common.Data.Aethernet;
using BOCCHI.Common.Data.CriticalEncounters;
using BOCCHI.Common.Data.KnowledgeCrystals;
using BOCCHI.Common.Data.Zones.Graph;
using BOCCHI.Common.Data.Zones.Graph.Factory;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.InstanceContent;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Ocelot.Extensions;
using Ocelot.Services.Logger;
using Ocelot.Services.Pathfinding;
using System.Numerics;
using Path = System.IO.Path;

namespace BOCCHI.Common.Data.Zones;

public abstract class BaseZone
(
    IObjectTable objects,
    IDalamudPluginInterface plugin,
    IGraphFactory graphs,
    IPathfinder pathfinder,
    ILogger logger,
    ZoneId zoneId
) : IZone
{
    protected abstract uint BasecampPlaceNameId { get; }

    public ZoneId ZoneId
    {
        get => zoneId;
    }

    public ushort TerritoryType => (ushort)ZoneId;

    public ushort ForkedTowerEventId => GetForkedTowerEventId();

    public bool IsOccultCrescentZone() => true;

    /// <summary>
    ///     True at expedition base camp. SubAreaPlaceNameId is unreliable (duplicate PlaceName
    ///     rows / lag), so also accept proximity to the main aetheryte — otherwise Return loops
    ///     forever after Demi-Return lands "in town".
    /// </summary>
    public bool IsInBasecamp()
    {
        if (GetCurrentSubAreaPlaceNameId() == BasecampPlaceNameId)
        {
            return true;
        }

        if (objects.LocalPlayer is not { } player)
        {
            return false;
        }

        return player.Position.Distance2D(GetAetherytePosition()) <= NavigationConstants.CampRadius;
    }

    public abstract AethernetData GetMainAetheryte();

    public abstract Vector3 GetAetherytePosition();

    public abstract Vector3 GetStartingPosition();

    public virtual List<AethernetData> GetAetherytes() => [];

    public virtual List<AethernetData> GetAethernetShards() => [];

    public virtual List<ActivityData> GetNormalFateData() => [];

    public virtual List<ActivityData> GetPotFateData() => [];

    public virtual List<ActivityData> GetCriticalEncounterData() => [];

    public virtual List<TreasureData> GetTreasureData() => [];

    public virtual Dictionary<int, List<PotChestData>> GetPotChestData() => [];

    public virtual List<PotChestData> GetRerollPotChestData() => [];

    // Authored chewed-carrot pads for Carrot Hunt (nearest-neighbor tour).
    public virtual List<CarrotData> GetCarrotData() => [];

    public virtual BuffZone? GetBuffZone() => null;

    public virtual List<Vector3> GetAuthoredKnowledgeCrystalCenters() => [];

    public virtual TreasureRoutePolicy GetTreasureRoutePolicy() => new();

    public List<KnowledgeCrystalData> GetNearbyKnowledgeCrystals()
    {
        if (objects.LocalPlayer is not { } player)
        {
            return [];
        }

        // Do not gate on IsInBasecamp() — SubAreaPlaceNameId often does not match the
        // authored BasecampPlaceNameId even while standing at camp. Same BaseId is also
        // used by some CE event objects, so require proximity to an aetheryte / shard
        // (or an authored crystal / camp buff point) rather than the main camp only.
        Vector3 playerPos = player.Position;
        const float playerRange = KnowledgeCrystalData.NearbySearchRange;
        const float aetheryteRange = 100f;
        float playerRangeSq = playerRange * playerRange;
        float aetheryteRangeSq = aetheryteRange * aetheryteRange;
        bool inForkedTower = IsInForkedTower();

        List<Vector3> anchors = [];
        foreach (AethernetData aetheryte in GetAetherytes())
        {
            anchors.Add(aetheryte.Position);
        }

        foreach (AethernetData shard in GetAethernetShards())
        {
            if (anchors.All(a => Vector3.DistanceSquared(a, shard.Position) > 1f))
            {
                anchors.Add(shard.Position);
            }
        }

        if (anchors.Count == 0)
        {
            anchors.Add(GetAetherytePosition());
        }

        if (GetBuffZone() is { } buffZone)
        {
            anchors.Add(buffZone.Center);
        }

        foreach (Vector3 authored in GetAuthoredKnowledgeCrystalCenters())
        {
            if (anchors.All(a => Vector3.DistanceSquared(a, authored) > 1f))
            {
                anchors.Add(authored);
            }
        }

        List<KnowledgeCrystalData> crystals = objects
            .Where(o => o is { ObjectKind: ObjectKind.EventObj, BaseId: KnowledgeCrystalData.BaseId })
            .Where(o => Vector3.DistanceSquared(o.Position, playerPos) <= playerRangeSq)
            .Where(o => inForkedTower
                        || anchors.Any(a => Vector3.DistanceSquared(o.Position, a) <= aetheryteRangeSq))
            .OrderBy(o => Vector3.DistanceSquared(o.Position, playerPos))
            .Select(o => new KnowledgeCrystalData
            {
                Position = o.Position
            })
            .ToList();

        // Authored camp buff point / tower crystals: still count when the live object is
        // missing / id-mismatched but the player is standing at the known buff site.
        List<Vector3> authoredSites = [];
        if (GetBuffZone() is { } zone)
        {
            authoredSites.Add(zone.Center);
        }

        authoredSites.AddRange(GetAuthoredKnowledgeCrystalCenters());

        foreach (Vector3 site in authoredSites)
        {
            if (Vector3.DistanceSquared(playerPos, site) > playerRangeSq)
            {
                continue;
            }

            if (crystals.Any(c => Vector3.DistanceSquared(c.Position, site) <= 25f))
            {
                continue;
            }

            crystals.Add(new KnowledgeCrystalData
            {
                Position = site
            });
        }

        return crystals
            .OrderBy(c => Vector3.DistanceSquared(c.Position, playerPos))
            .ToList();
    }

    public unsafe bool IsInForkedTower()
    {
        DynamicEventContainer* dec = DynamicEventContainer.GetInstance();

        return dec != null && dec->CurrentEventId == GetForkedTowerEventId();
    }

    private ZoneGraph? cachedGraph;
    private Task<ZoneGraph>? graphLoadTask;
    private readonly object graphGate = new();

    private ZoneGraphLoadState graphLoadState = ZoneGraphLoadState.Idle;

    private ZoneGraphSource graphSource = ZoneGraphSource.None;

    /// <summary>Schema for on-disk / shipped zone path maps. Bump with Data/ZoneGraphs files.</summary>
    private const int GraphSchemaVersion = 8;

    public ZoneGraphLoadState GraphLoadState => graphLoadState;

    public ZoneGraphSource GraphSource => graphSource;

    public Task<ZoneGraph> GetGraph()
    {
        if (cachedGraph != null)
        {
            return Task.FromResult(cachedGraph);
        }

        lock (graphGate)
        {
            if (cachedGraph != null)
            {
                return Task.FromResult(cachedGraph);
            }

            return graphLoadTask ??= LoadOrBuildGraphAsync();
        }
    }

    public void ApplyCriticalEncounterCombat(int eventId, float combatRadius, ActivityAreaShape shape)
    {
        if (cachedGraph == null || combatRadius <= 0f)
        {
            return;
        }

        foreach (Node node in cachedGraph.GetActivityNodes())
        {
            if (node.Type != NodeType.CriticalEncounter
                || node.Metadata is not ActivityNodeMetadata meta
                || meta.Id != eventId)
            {
                continue;
            }

            meta.CombatRadius = SanitizeCombatRadius(combatRadius);
            meta.AreaShape = shape;
        }
    }

    private static float SanitizeCombatRadius(float combatRadius) =>
        combatRadius > 0f && combatRadius <= CriticalEncounter.MaxRegistrationRadius
            ? combatRadius
            : CriticalEncounter.FallbackRegistrationRadius;

    public void InvalidateGraph(string? reason = null)
    {
        lock (graphGate)
        {
            cachedGraph = null;
            graphLoadTask = null;
            graphLoadState = ZoneGraphLoadState.Idle;
            graphSource = ZoneGraphSource.None;
        }

        string path = Path.Combine(
            plugin.GetPluginConfigDirectory(),
            "zone_graphs",
            $"{TerritoryType}.v{GraphSchemaVersion}.json");

        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            logger.Info(
                "Invalidated zone path map for territory {Territory}{Reason}",
                TerritoryType,
                string.IsNullOrEmpty(reason) ? "" : $": {reason}");
        }
        catch (Exception ex)
        {
            logger.Warning(ex, "Failed to delete zone path map cache ({Path})", path);
        }
    }

    private async Task<ZoneGraph> LoadOrBuildGraphAsync()
    {
        graphLoadState = ZoneGraphLoadState.Loading;
        graphSource = ZoneGraphSource.None;

        string dir = Path.Combine(plugin.GetPluginConfigDirectory(), "zone_graphs");
        Directory.CreateDirectory(dir);

        // Bump GraphSchemaVersion when walk-cost / edge semantics or wired nodes change.
        string fileName = $"{TerritoryType}.v{GraphSchemaVersion}.json";
        string path = Path.Combine(dir, fileName);

        ZoneGraph? cached = await TryLoadGraphAsync(path);
        ZoneGraph? shipped = await TryLoadShippedGraphAsync(fileName);

        if (cached != null && !cached.CoversZoneActivities(this))
        {
            logger.Warning(
                "Zone path map cache is stale or incomplete for territory {Territory} — discarding",
                TerritoryType);
            TryDeleteFile(path);
            cached = null;
        }

        // Prefer a more complete bundled map over an older saved cache.
        if (cached != null
            && shipped != null
            && shipped.CoversZoneActivities(this)
            && shipped.CountRoutableActivities() > cached.CountRoutableActivities())
        {
            logger.Info(
                "Bundled path map is more complete than cache for territory {Territory} — replacing saved map",
                TerritoryType);
            cached = null;
            TryDeleteFile(path);
        }

        if (cached != null)
        {
            logger.Debug("Loaded zone graph from path: " + path);
            cachedGraph = cached;
            graphSource = ZoneGraphSource.Cache;
            graphLoadState = ZoneGraphLoadState.Ready;
            return cached;
        }

        if (shipped != null && shipped.CoversZoneActivities(this))
        {
            logger.Info($"Using shipped zone graph for territory {TerritoryType}");
            await File.WriteAllTextAsync(path, shipped.ToJson());
            cachedGraph = shipped;
            graphSource = ZoneGraphSource.Shipped;
            graphLoadState = ZoneGraphLoadState.Ready;
            return shipped;
        }

        if (File.Exists(path))
        {
            TryDeleteFile(path);
        }

        graphLoadState = ZoneGraphLoadState.Building;
        logger.Info($"Building zone graph for territory {TerritoryType} (one-time; Automator waits until done)");
        GraphConfig config = new(pathfinder, logger);
        ZoneGraph built = await graphs.BuildAsync(config, this);
        built.ClearCriticalEncounterCombatRadii();
        logger.Debug("Writing zone graph to: " + path);
        await File.WriteAllTextAsync(path, built.ToJson());

        cachedGraph = built;
        graphSource = ZoneGraphSource.Built;
        graphLoadState = ZoneGraphLoadState.Ready;
        return built;
    }

    private async Task<ZoneGraph?> TryLoadShippedGraphAsync(string fileName)
    {
        string? shippedPath = GetShippedZoneGraphPath(fileName);
        return shippedPath == null ? null : await TryLoadGraphAsync(shippedPath);
    }

    private async Task<ZoneGraph?> TryLoadGraphAsync(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            string json = await File.ReadAllTextAsync(path);
            ZoneGraph? loaded = ZoneGraph.FromJson(json);
            if (loaded is not { } graph || !graph.IsUsableForRouting())
            {
                return null;
            }

            graph.ClearCriticalEncounterCombatRadii();
            return graph;
        }
        catch (Exception ex)
        {
            logger.Warning(ex, "Failed to load zone graph ({Path})", path);
            return null;
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Rebuild / seed overwrites; delete is best-effort.
        }
    }

    private string? GetShippedZoneGraphPath(string fileName)
    {
        string? pluginDir = plugin.AssemblyLocation.DirectoryName;
        if (string.IsNullOrEmpty(pluginDir))
        {
            pluginDir = Path.GetDirectoryName(plugin.GetType().Assembly.Location);
        }

        if (string.IsNullOrEmpty(pluginDir))
        {
            return null;
        }

        string path = Path.Combine(pluginDir, "Data", "ZoneGraphs", fileName);
        return File.Exists(path) ? path : null;
    }

    private unsafe uint GetCurrentSubAreaPlaceNameId()
    {
        TerritoryInfo* info = TerritoryInfo.Instance();
        return info == null ? 0 : info->SubAreaPlaceNameId;
    }

    protected abstract ushort GetForkedTowerEventId();
}
