using BOCCHI.Common.Config;
using BOCCHI.Common.Data.Zones;
using BOCCHI.Common.Data.Zones.Graph;
using BOCCHI.Common.Services;
using Dalamud.Plugin;
using Ocelot.Lifecycle;
using Ocelot.Services.Logger;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BOCCHI.Treasure.Services;

public sealed class PotChestLocationSyncService
(
    TreasureConfig config,
    IZoneProvider zones,
    IDalamudPluginInterface plugin,
    ILogger<PotChestLocationSyncService> logger
) : CrowdsourceLocationSync<AcceptedPotChestLocation>, IOnUpdate
{
    public const string ApiBaseUrl = PotCycleSyncService.ApiBaseUrl;

    public const string ApiUrl = ApiBaseUrl + "/api/v1/pot-chest-locations";

    public UpdateLimit UpdateLimit =>
        new()
        {
            Mode = UpdateLimitMode.Milliseconds,
            Limit = 1000
        };

    public void EnsureFreshForFarm()
    {
        if (!config.EnableSharedMaps || !zones.GetZone().IsOccultCrescentZone())
        {
            return;
        }

        StartCatalogRefresh(zones.GetZone().TerritoryType, force: true);
    }

    private readonly Lock poolsGate = new();

    private readonly Dictionary<(ushort Territory, int FateId, bool Reroll), IReadOnlyList<PotChestData>> pools = [];

    private (ushort Catalog, object Accepted, bool Shared) poolsKey;

    public IReadOnlyList<PotChestData> GetPrimaryPads(IZone zone, int fateId) =>
        CachedPool(zone, fateId, isReroll: false, () =>
        {
            if (!zone.GetPotChestData().TryGetValue(fateId, out List<PotChestData>? baked))
            {
                baked = [];
            }

            return MergePool(zone, fateId, isReroll: false, baked);
        });

    public IReadOnlyList<PotChestData> GetRerollPads(IZone zone) =>
        CachedPool(zone, 0, isReroll: true, () => MergeRerollPool(zone, zone.GetRerollPotChestData()));

    // The pot chest farm asks every frame; the merge is only redone when the shared pad list changes.
    private IReadOnlyList<PotChestData> CachedPool(IZone zone, int fateId, bool isReroll, Func<IReadOnlyList<PotChestData>> build)
    {
        lock (poolsGate)
        {
            (ushort, object, bool) key = (catalogTerritory, accepted, config.EnableSharedMaps);
            if (poolsKey.Catalog != key.Item1 || !ReferenceEquals(poolsKey.Accepted, key.Item2) || poolsKey.Shared != key.Item3)
            {
                pools.Clear();
                poolsKey = key;
            }

            if (!pools.TryGetValue((zone.TerritoryType, fateId, isReroll), out IReadOnlyList<PotChestData>? pool))
            {
                pool = build();
                pools[(zone.TerritoryType, fateId, isReroll)] = pool;
            }

            return pool;
        }
    }

    public bool CanRunSmart(IZone zone, int fateId) =>
        zone.IsPotFate(fateId) && GetPrimaryPads(zone, fateId).Count > 0;

    public void Submit(int potFateId, bool isReroll, Vector3 position)
    {
        if (!config.EnableSharedMaps || !zones.GetZone().IsOccultCrescentZone())
        {
            return;
        }

        if (TreasurePathing.IsUnloadAltitude(position))
        {
            return;
        }

        ushort territory = zones.GetZone().TerritoryType;
        string key = CrowdsourceSyncHttp.PositionKey(territory, potFateId, isReroll, position);
        if (queuedKeys.Contains(key) || submittedKeys.Contains(key))
        {
            return;
        }

        queue.Enqueue(new PendingSubmit(territory, position, key, PotFateId: potFateId, IsReroll: isReroll));
        queuedKeys.Add(key);
    }

    public void Update()
    {
        ApplyCompletedWork();

        if (!config.EnableSharedMaps)
        {
            if (accepted.Count > 0)
            {
                accepted = [];
                catalogTerritory = 0;
            }

            return;
        }

        IZone zone = zones.GetZone();
        if (!zone.IsOccultCrescentZone())
        {
            return;
        }

        StartNextUpload();
        StartCatalogRefresh(zone.TerritoryType, force: false);
    }

    private IReadOnlyList<PotChestData> MergePool(
        IZone zone,
        int fateId,
        bool isReroll,
        IReadOnlyList<PotChestData> baked)
    {
        if (!config.EnableSharedMaps
            || catalogTerritory != zone.TerritoryType
            || accepted.Count == 0)
        {
            return baked;
        }

        List<AcceptedPotChestLocation> remotes = accepted
            .Where(a => a.PotFateId == fateId && a.IsReroll == isReroll)
            .ToList();
        return remotes.Count == 0 ? baked.ToList() : PotChestPadCatalog.Merge(baked, remotes);
    }

    private IReadOnlyList<PotChestData> MergeRerollPool(IZone zone, IReadOnlyList<PotChestData> baked)
    {
        if (!config.EnableSharedMaps
            || catalogTerritory != zone.TerritoryType
            || accepted.Count == 0)
        {
            return baked;
        }

        List<AcceptedPotChestLocation> remotes = accepted.Where(a => a.IsReroll).ToList();
        return remotes.Count == 0 ? baked.ToList() : PotChestPadCatalog.Merge(baked, remotes);
    }

    protected override string UploadUrl => ApiUrl;
    protected override string CatalogUrl => ApiUrl;

    protected override string BuildPayload(PendingSubmit pending) => JsonSerializer.Serialize(new
    {
        territoryId = (int)pending.TerritoryId,
        potFateId = pending.PotFateId,
        isReroll = pending.IsReroll,
        worldX = pending.Position.X,
        worldY = pending.Position.Y,
        worldZ = pending.Position.Z,
        installationHash = InstallationId.GetHash(plugin),
        pluginVersion = typeof(PotChestLocationSyncService).Assembly.GetName().Version?.ToString() ?? "0",
        observedAtUtc = DateTime.UtcNow.ToString("O"),
    });

    protected override IReadOnlyList<AcceptedPotChestLocation> ParseCatalog(string body, ushort territory)
    {
        CatalogResponse? parsed = JsonSerializer.Deserialize<CatalogResponse>(body, CrowdsourceSyncHttp.JsonOptions);
        List<AcceptedPotChestLocation> locations = [];
        if (parsed?.Locations != null)
        {
            foreach (CatalogEntry entry in parsed.Locations)
            {
                if (entry.Position == null || entry.TerritoryId != territory)
                {
                    continue;
                }

                Vector3 position = new(entry.Position.X, entry.Position.Y, entry.Position.Z);
                if (TreasurePathing.IsUnloadAltitude(position))
                {
                    continue;
                }

                locations.Add(new AcceptedPotChestLocation(
                    entry.CandidateId,
                    (ushort)entry.TerritoryId,
                    entry.PotFateId,
                    entry.IsReroll,
                    position));
            }
        }

        return locations;
    }

    protected override void LogUploaded(PendingSubmit pending)
    {
        logger.Info(
            "uploaded fate={Fate} reroll={Reroll} pos=({X:F2},{Y:F2},{Z:F2})",
            pending.PotFateId,
            pending.IsReroll,
            pending.Position.X,
            pending.Position.Y,
            pending.Position.Z);
    }

    protected override void LogCatalog(ushort territory, int count) =>
        logger.Info("catalog territory={Territory} locations={Count}", territory, count);

    protected override void LogWarning(string message, string value) => logger.Warn(message, value);

    private sealed class CatalogResponse
    {
        [JsonPropertyName("locations")]
        public List<CatalogEntry>? Locations { get; set; }
    }

    private sealed class CatalogEntry
    {
        [JsonPropertyName("candidateId")]
        public int CandidateId { get; set; }

        [JsonPropertyName("territoryId")]
        public int TerritoryId { get; set; }

        [JsonPropertyName("potFateId")]
        public int PotFateId { get; set; }

        [JsonPropertyName("isReroll")]
        public bool IsReroll { get; set; }

        [JsonPropertyName("position")]
        public PositionDto? Position { get; set; }
    }

    private sealed class PositionDto
    {
        [JsonPropertyName("x")]
        public float X { get; set; }

        [JsonPropertyName("y")]
        public float Y { get; set; }

        [JsonPropertyName("z")]
        public float Z { get; set; }
    }
}
