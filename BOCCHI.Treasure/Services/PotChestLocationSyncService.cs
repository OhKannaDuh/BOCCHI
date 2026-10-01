using BOCCHI.Common.Config;
using BOCCHI.Common.Data.Zones;
using BOCCHI.Common.Data.Zones.Graph;
using BOCCHI.Common.Services;
using Dalamud.Plugin;
using Ocelot.Lifecycle;
using Ocelot.Services.Logger;
using System.Net.Http;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BOCCHI.Treasure.Services;

public sealed class PotChestLocationSyncService
(
    TreasureConfig config,
    IZoneProvider zones,
    IDalamudPluginInterface plugin,
    ILogger<PotChestLocationSyncService> logger
) : IOnUpdate
{
    public const string ApiBaseUrl = PotCycleSyncService.ApiBaseUrl;

    public const string ApiUrl = ApiBaseUrl + "/api/v1/pot-chest-locations";

    private readonly Queue<PendingSubmit> queue = new();

    private readonly HashSet<string> queuedKeys = new(StringComparer.Ordinal);

    private readonly HashSet<string> submittedKeys = new(StringComparer.Ordinal);

    private DateTime nextUploadAttemptUtc = DateTime.MinValue;

    private DateTime nextCatalogFetchUtc = DateTime.MinValue;

    private ushort catalogTerritory;

    private bool uploadInFlight;

    private bool catalogInFlight;

    private UploadOutcome? completedUpload;

    private CatalogOutcome? completedCatalog;

    private IReadOnlyList<AcceptedPotChestLocation> accepted = [];

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

    public IReadOnlyList<PotChestData> GetPrimaryPads(IZone zone, int fateId)
    {
        if (!zone.GetPotChestData().TryGetValue(fateId, out List<PotChestData>? baked))
        {
            baked = [];
        }

        return MergePool(zone, fateId, isReroll: false, baked);
    }

    public IReadOnlyList<PotChestData> GetRerollPads(IZone zone)
    {
        List<PotChestData> baked = zone.GetRerollPotChestData();
        return MergeRerollPool(zone, baked);
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

        queue.Enqueue(new PendingSubmit(territory, potFateId, isReroll, position, key));
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

    private void ApplyCompletedWork()
    {
        UploadOutcome? upload = Interlocked.Exchange(ref completedUpload, null);
        if (upload != null)
        {
            uploadInFlight = false;
            if (upload.Success)
            {
                if (queue.Count > 0 && queue.Peek().Key == upload.Key)
                {
                    PendingSubmit done = queue.Dequeue();
                    queuedKeys.Remove(done.Key);
                    submittedKeys.Add(done.Key);
                }

                nextUploadAttemptUtc = DateTime.UtcNow;
                logger.Info(
                    "[PotChestLocationSync] uploaded fate={Fate} reroll={Reroll} pos=({X:F2},{Y:F2},{Z:F2})",
                    upload.PotFateId,
                    upload.IsReroll,
                    upload.X,
                    upload.Y,
                    upload.Z);
            }
            else
            {
                nextUploadAttemptUtc = DateTime.UtcNow + CrowdsourceSyncHttp.RetryDelay;
                if (upload.Error is { } uploadError)
                {
                    logger.Warn("[PotChestLocationSync] upload failed: {Message}", uploadError);
                }
                else
                {
                    logger.Warn("[PotChestLocationSync] upload rejected: {Status}", upload.Status ?? "?");
                }
            }
        }

        CatalogOutcome? catalog = Interlocked.Exchange(ref completedCatalog, null);
        if (catalog == null)
        {
            return;
        }

        catalogInFlight = false;
        if (catalog.Success)
        {
            accepted = catalog.Locations;
            catalogTerritory = catalog.TerritoryId;
            nextCatalogFetchUtc = DateTime.UtcNow + CrowdsourceSyncHttp.CatalogRefreshInterval;
            logger.Info(
                "[PotChestLocationSync] catalog territory={Territory} locations={Count}",
                catalog.TerritoryId,
                accepted.Count);
        }
        else
        {
            nextCatalogFetchUtc = DateTime.UtcNow + CrowdsourceSyncHttp.RetryDelay;
            if (catalog.Error is { } catalogError)
            {
                logger.Warn("[PotChestLocationSync] catalog failed: {Message}", catalogError);
            }
            else
            {
                logger.Warn("[PotChestLocationSync] catalog rejected: {Status}", catalog.Status ?? "?");
            }
        }
    }

    private void StartNextUpload()
    {
        if (uploadInFlight || queue.Count == 0 || DateTime.UtcNow < nextUploadAttemptUtc)
        {
            return;
        }

        PendingSubmit pending = queue.Peek();
        string json = JsonSerializer.Serialize(new
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

        uploadInFlight = true;
        _ = UploadAsync(pending, json);
    }

    private async Task UploadAsync(PendingSubmit pending, string json)
    {
        try
        {
            using HttpRequestMessage request = new(HttpMethod.Post, ApiUrl)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };

            using HttpResponseMessage response = await CrowdsourceSyncHttp.Http.SendAsync(request).ConfigureAwait(false);
            Interlocked.Exchange(
                ref completedUpload,
                response.IsSuccessStatusCode
                    ? UploadOutcome.Ok(pending)
                    : UploadOutcome.Rejected(pending, response.StatusCode.ToString()));
        }
        catch (Exception ex)
        {
            Interlocked.Exchange(ref completedUpload, UploadOutcome.Failed(pending, ex.Message));
        }
    }

    private void StartCatalogRefresh(ushort territory, bool force)
    {
        if (catalogInFlight)
        {
            return;
        }

        if (!force
            && catalogTerritory == territory
            && DateTime.UtcNow < nextCatalogFetchUtc
            && accepted.Count > 0)
        {
            return;
        }

        if (!force && DateTime.UtcNow < nextCatalogFetchUtc && catalogTerritory == territory)
        {
            return;
        }

        catalogInFlight = true;
        _ = FetchCatalogAsync(territory);
    }

    private async Task FetchCatalogAsync(ushort territory)
    {
        try
        {
            string url = $"{ApiUrl}?territoryId={territory}";
            using HttpRequestMessage request = new(HttpMethod.Get, url);
            using HttpResponseMessage response = await CrowdsourceSyncHttp.Http.SendAsync(request).ConfigureAwait(false);
            string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                Interlocked.Exchange(
                    ref completedCatalog,
                    CatalogOutcome.Rejected(territory, response.StatusCode.ToString()));
                return;
            }

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

            Interlocked.Exchange(ref completedCatalog, CatalogOutcome.Ok(territory, locations));
        }
        catch (Exception ex)
        {
            Interlocked.Exchange(ref completedCatalog, CatalogOutcome.Failed(territory, ex.Message));
        }
    }

    private readonly record struct PendingSubmit(
        ushort TerritoryId,
        int PotFateId,
        bool IsReroll,
        Vector3 Position,
        string Key);

    private sealed class UploadOutcome
    {
        public required string Key { get; init; }

        public required int PotFateId { get; init; }

        public required bool IsReroll { get; init; }

        public required float X { get; init; }

        public required float Y { get; init; }

        public required float Z { get; init; }

        public required bool Success { get; init; }

        public string? Status { get; init; }

        public string? Error { get; init; }

        public static UploadOutcome Ok(PendingSubmit pending) => new()
        {
            Key = pending.Key,
            PotFateId = pending.PotFateId,
            IsReroll = pending.IsReroll,
            X = pending.Position.X,
            Y = pending.Position.Y,
            Z = pending.Position.Z,
            Success = true,
        };

        public static UploadOutcome Rejected(PendingSubmit pending, string status) => new()
        {
            Key = pending.Key,
            PotFateId = pending.PotFateId,
            IsReroll = pending.IsReroll,
            X = pending.Position.X,
            Y = pending.Position.Y,
            Z = pending.Position.Z,
            Success = false,
            Status = status,
        };

        public static UploadOutcome Failed(PendingSubmit pending, string error) => new()
        {
            Key = pending.Key,
            PotFateId = pending.PotFateId,
            IsReroll = pending.IsReroll,
            X = pending.Position.X,
            Y = pending.Position.Y,
            Z = pending.Position.Z,
            Success = false,
            Error = error,
        };
    }

    private sealed class CatalogOutcome
    {
        public required ushort TerritoryId { get; init; }

        public required bool Success { get; init; }

        public IReadOnlyList<AcceptedPotChestLocation> Locations { get; init; } = [];

        public string? Status { get; init; }

        public string? Error { get; init; }

        public static CatalogOutcome Ok(
            ushort territory,
            IReadOnlyList<AcceptedPotChestLocation> locations) => new()
        {
            TerritoryId = territory,
            Success = true,
            Locations = locations,
        };

        public static CatalogOutcome Rejected(ushort territory, string status) => new()
        {
            TerritoryId = territory,
            Success = false,
            Status = status,
        };

        public static CatalogOutcome Failed(ushort territory, string error) => new()
        {
            TerritoryId = territory,
            Success = false,
            Error = error,
        };
    }

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
