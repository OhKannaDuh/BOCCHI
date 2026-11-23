using BOCCHI.Common.Config;
using BOCCHI.Common.Data.Zones;
using BOCCHI.Common.Services;
using Dalamud.Plugin;
using Ocelot.Lifecycle;
using Ocelot.Services.Logger;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BOCCHI.Treasure.Services;

public readonly record struct CrowdsourcedCofferCandidate(
    int CandidateId,
    ushort TerritoryId,
    uint DataId,
    Vector3 Position);

public sealed class CofferLocationSyncService
(
    TreasureConfig config,
    IZoneProvider zones,
    IDalamudPluginInterface plugin,
    ILogger<CofferLocationSyncService> logger
) : CrowdsourceLocationSync<CrowdsourcedCofferCandidate>, IOnUpdate
{
    public const string ApiBaseUrl = PotCycleSyncService.ApiBaseUrl;

    public const string ObservationsUrl = ApiBaseUrl + "/api/v1/observations";

    public const string CandidatesUrl = ApiBaseUrl + "/api/v1/candidates";

    public const float MatchRadius = 3.5f;

    public const float MatchRadiusSq = MatchRadius * MatchRadius;

    public UpdateLimit UpdateLimit =>
        new()
        {
            Mode = UpdateLimitMode.Milliseconds,
            Limit = 1000
        };

    public IReadOnlyList<CrowdsourcedCofferCandidate> GetAcceptedForCurrentZone()
    {
        if (!config.EnableSharedMaps)
        {
            return [];
        }

        ushort territory = zones.GetZone().TerritoryType;
        return catalogTerritory == territory ? accepted : [];
    }

    public void EnsureFreshForHunt()
    {
        if (!config.EnableSharedMaps || !zones.GetZone().IsOccultCrescentZone())
        {
            return;
        }

        // Route planning calls this after every coffer; a forced download each time floods the server.
        ushort territory = zones.GetZone().TerritoryType;
        bool stale = catalogTerritory != territory || DateTime.UtcNow - lastForcedRefreshUtc > TimeSpan.FromMinutes(2);
        if (stale)
        {
            lastForcedRefreshUtc = DateTime.UtcNow;
        }

        StartCatalogRefresh(territory, force: stale);
    }

    private DateTime lastForcedRefreshUtc;

    public void Submit(uint dataId, float x, float y, float z, string cofferType)
    {
        if (!config.EnableSharedMaps || !zones.GetZone().IsOccultCrescentZone())
        {
            return;
        }

        if (TreasurePathing.IsUnloadAltitude(y))
        {
            return;
        }

        ushort territory = zones.GetZone().TerritoryType;
        Vector3 position = new(x, y, z);
        string key = CrowdsourceSyncHttp.PositionKey(territory, dataId, position);
        if (queuedKeys.Contains(key) || submittedKeys.Contains(key))
        {
            return;
        }

        queue.Enqueue(new PendingSubmit(territory, position, key, DataId: dataId, CofferType: cofferType));
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

    protected override string UploadUrl => ObservationsUrl;
    protected override string CatalogUrl => CandidatesUrl;

    protected override string BuildPayload(PendingSubmit pending) => JsonSerializer.Serialize(new
    {
        territoryId = (int)pending.TerritoryId,
        dataId = pending.DataId,
        worldX = pending.Position.X,
        worldY = pending.Position.Y,
        worldZ = pending.Position.Z,
        cofferType = pending.CofferType,
        installationHash = InstallationId.GetHash(plugin),
        pluginVersion = typeof(CofferLocationSyncService).Assembly.GetName().Version?.ToString() ?? "0",
        observedAtUtc = DateTime.UtcNow.ToString("O"),
    });

    protected override IReadOnlyList<CrowdsourcedCofferCandidate> ParseCatalog(string body, ushort territory)
    {
        CatalogResponse? parsed = JsonSerializer.Deserialize<CatalogResponse>(body, CrowdsourceSyncHttp.JsonOptions);
        List<CrowdsourcedCofferCandidate> locations = [];
        if (parsed?.Candidates != null)
        {
            foreach (CatalogCandidate entry in parsed.Candidates)
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

                locations.Add(new CrowdsourcedCofferCandidate(
                    entry.CandidateId,
                    (ushort)entry.TerritoryId,
                    (uint)entry.DataId,
                    position));
            }
        }

        return locations;
    }

    protected override void LogUploaded(PendingSubmit pending)
    {
        logger.Info(
            "uploaded dataId={DataId} pos=({X:F2},{Y:F2},{Z:F2})",
            pending.DataId,
            pending.Position.X,
            pending.Position.Y,
            pending.Position.Z);
    }

    protected override void LogCatalog(ushort territory, int count) =>
        logger.Info("catalog territory={Territory} candidates={Count}", territory, count);

    protected override void LogWarning(string message, string value) => logger.Warn(message, value);

    private sealed class CatalogResponse
    {
        [JsonPropertyName("candidates")]
        public List<CatalogCandidate>? Candidates { get; set; }
    }

    private sealed class CatalogCandidate
    {
        [JsonPropertyName("candidateId")]
        public int CandidateId { get; set; }

        [JsonPropertyName("territoryId")]
        public int TerritoryId { get; set; }

        [JsonPropertyName("dataId")]
        public int DataId { get; set; }

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
