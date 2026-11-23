using BOCCHI.Common.Config;
using BOCCHI.Common.Data;
using BOCCHI.Common.Data.Zones;
using BOCCHI.Common.Data.Zones.Graph;
using BOCCHI.Common.Services;
using BOCCHI.Treasure.Data;
using Dalamud.Plugin;
using Ocelot.Lifecycle;
using Ocelot.Services.Logger;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BOCCHI.Treasure.Services;

public sealed class CarrotLocationSyncService
(
    TreasureConfig config,
    IZoneProvider zones,
    ICarrotTracker carrots,
    IDalamudPluginInterface plugin,
    ILogger<CarrotLocationSyncService> logger
) : CrowdsourceLocationSync<AcceptedCarrotLocation>, IOnUpdate
{
    public const string ApiBaseUrl = PotCycleSyncService.ApiBaseUrl;

    public const string ApiUrl = ApiBaseUrl + "/api/v1/carrot-locations";

    public int Order => -10;

    public UpdateLimit UpdateLimit =>
        new()
        {
            Mode = UpdateLimitMode.Milliseconds,
            Limit = 1000
        };

    public IReadOnlyList<CarrotData> GetHuntPads(IZone zone)
    {
        List<CarrotData> baked = zone.GetCarrotData();
        if (!config.EnableSharedMaps
            || baked.Count == 0
            || catalogTerritory != zone.TerritoryType
            || accepted.Count == 0)
        {
            return baked;
        }

        return CarrotPadCatalog.Merge(baked, accepted);
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

        ushort territory = zone.TerritoryType;
        EnqueueSightedCarrots(territory);
        StartNextUpload();
        StartCatalogRefresh(territory);
    }

    private void EnqueueSightedCarrots(ushort territory)
    {
        foreach (Carrot carrot in carrots.Carrots)
        {
            if (!carrot.IsValid())
            {
                continue;
            }

            Vector3 position = carrot.GetPosition();
            if (TreasurePathing.IsUnloadAltitude(position))
            {
                continue;
            }

            string key = CrowdsourceSyncHttp.PositionKey(territory, position);
            if (queuedKeys.Contains(key) || submittedKeys.Contains(key))
            {
                continue;
            }

            queue.Enqueue(new PendingSubmit(territory, position, key));
            queuedKeys.Add(key);
        }
    }

    protected override string UploadUrl => ApiUrl;
    protected override string CatalogUrl => ApiUrl;

    protected override string BuildPayload(PendingSubmit pending) => JsonSerializer.Serialize(new
    {
        territoryId = (int)pending.TerritoryId,
        worldX = pending.Position.X,
        worldY = pending.Position.Y,
        worldZ = pending.Position.Z,
        objectBaseId = (int)OccultObjectType.Carrot,
        installationHash = InstallationId.GetHash(plugin),
        pluginVersion = typeof(CarrotLocationSyncService).Assembly.GetName().Version?.ToString() ?? "0",
        observedAtUtc = DateTime.UtcNow.ToString("O"),
    });

    protected override IReadOnlyList<AcceptedCarrotLocation> ParseCatalog(string body, ushort territory)
    {
        CarrotCatalogResponse? parsed = JsonSerializer.Deserialize<CarrotCatalogResponse>(body, CrowdsourceSyncHttp.JsonOptions);
        List<AcceptedCarrotLocation> locations = parsed?.Locations?
            .Where(l => l.TerritoryId == territory && l.Position != null)
            .Select(l => new AcceptedCarrotLocation(
                l.CandidateId,
                (ushort)l.TerritoryId,
                new Vector3(l.Position!.X, l.Position.Y, l.Position.Z)))
            .Where(l => !TreasurePathing.IsUnloadAltitude(l.Position))
            .ToList()
            ?? [];

        return locations;
    }

    protected override void LogUploaded(PendingSubmit pending)
    {
        logger.Info(
            "uploaded territory={Territory} pos=({X:F2},{Y:F2},{Z:F2})",
            pending.TerritoryId,
            pending.Position.X,
            pending.Position.Y,
            pending.Position.Z);
    }

    protected override void LogCatalog(ushort territory, int count) =>
        logger.Info("catalog territory={Territory} locations={Count}", territory, count);

    protected override void LogWarning(string message, string value) => logger.Warn(message, value);

    private sealed class CarrotCatalogResponse
    {
        [JsonPropertyName("locations")]
        public List<CarrotLocationDto>? Locations { get; set; }
    }

    private sealed class CarrotLocationDto
    {
        [JsonPropertyName("candidateId")]
        public int CandidateId { get; set; }

        [JsonPropertyName("territoryId")]
        public int TerritoryId { get; set; }

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
