using BOCCHI.Common.Config;
using BOCCHI.Common.Data;
using BOCCHI.Common.Data.Zones;
using BOCCHI.Common.Data.Zones.Graph;
using BOCCHI.Common.Services;
using BOCCHI.Treasure.Data;
using Dalamud.Plugin;
using Ocelot.Lifecycle;
using Ocelot.Services.Logger;
using System.Net.Http;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BOCCHI.Treasure.Services;

/// <summary>
///     Fetches the accepted chewed-carrot catalog for Carrot Hunt and anonymously uploads
///     sightings when shared maps are enabled. HTTP runs off the framework thread.
/// </summary>
public sealed class CarrotLocationSyncService
(
    TreasureConfig config,
    IZoneProvider zones,
    ICarrotTracker carrots,
    IDalamudPluginInterface plugin,
    ILogger<CarrotLocationSyncService> logger
) : IOnUpdate
{
    public const string ApiBaseUrl = PotCycleSyncService.ApiBaseUrl;

    public const string ApiUrl = ApiBaseUrl + "/api/v1/carrot-locations";

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

    private IReadOnlyList<AcceptedCarrotLocation> acceptedLocations = [];

    /// <summary>After <see cref="CarrotTracker"/> (default Order 0).</summary>
    public int Order => -10;

    public UpdateLimit UpdateLimit =>
        new()
        {
            Mode = UpdateLimitMode.Milliseconds,
            Limit = 1000
        };

    /// <summary>Baked pads plus any worker-accepted pads not already nearby. Offline / share off → baked only.</summary>
    public IReadOnlyList<CarrotData> GetHuntPads(IZone zone)
    {
        List<CarrotData> baked = zone.GetCarrotData();
        if (!config.EnableSharedMaps
            || baked.Count == 0
            || catalogTerritory != zone.TerritoryType
            || acceptedLocations.Count == 0)
        {
            return baked;
        }

        return CarrotPadCatalog.Merge(baked, acceptedLocations);
    }

    public void Update()
    {
        ApplyCompletedWork();

        if (!config.EnableSharedMaps)
        {
            if (acceptedLocations.Count > 0)
            {
                acceptedLocations = [];
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
                    "[CarrotLocationSync] uploaded territory={Territory} pos=({X:F2},{Y:F2},{Z:F2})",
                    upload.TerritoryId,
                    upload.X,
                    upload.Y,
                    upload.Z);
            }
            else
            {
                nextUploadAttemptUtc = DateTime.UtcNow + CrowdsourceSyncHttp.RetryDelay;
                if (upload.Error is { } uploadError)
                {
                    logger.Warn("[CarrotLocationSync] upload failed: {Message}", uploadError);
                }
                else
                {
                    logger.Warn("[CarrotLocationSync] upload rejected: {Status}", upload.Status ?? "?");
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
            acceptedLocations = catalog.Locations;
            catalogTerritory = catalog.TerritoryId;
            nextCatalogFetchUtc = DateTime.UtcNow + CrowdsourceSyncHttp.CatalogRefreshInterval;
            logger.Info(
                "[CarrotLocationSync] catalog territory={Territory} locations={Count}",
                catalog.TerritoryId,
                acceptedLocations.Count);
        }
        else
        {
            nextCatalogFetchUtc = DateTime.UtcNow + CrowdsourceSyncHttp.RetryDelay;
            if (catalog.Error is { } catalogError)
            {
                logger.Warn("[CarrotLocationSync] catalog failed: {Message}", catalogError);
            }
            else
            {
                logger.Warn("[CarrotLocationSync] catalog rejected: {Status}", catalog.Status ?? "?");
            }
        }
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
            string key = CrowdsourceSyncHttp.PositionKey(territory, position);
            if (queuedKeys.Contains(key) || submittedKeys.Contains(key))
            {
                continue;
            }

            queue.Enqueue(new PendingSubmit(territory, position, key));
            queuedKeys.Add(key);
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
            worldX = pending.Position.X,
            worldY = pending.Position.Y,
            worldZ = pending.Position.Z,
            objectBaseId = (int)OccultObjectType.Carrot,
            installationHash = InstallationId.GetHash(plugin),
            pluginVersion = typeof(CarrotLocationSyncService).Assembly.GetName().Version?.ToString() ?? "0",
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

    private void StartCatalogRefresh(ushort territory)
    {
        if (catalogInFlight)
        {
            return;
        }

        if (catalogTerritory == territory
            && DateTime.UtcNow < nextCatalogFetchUtc
            && acceptedLocations.Count > 0)
        {
            return;
        }

        if (DateTime.UtcNow < nextCatalogFetchUtc && catalogTerritory == territory)
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

            CarrotCatalogResponse? parsed = JsonSerializer.Deserialize<CarrotCatalogResponse>(body, CrowdsourceSyncHttp.JsonOptions);
            List<AcceptedCarrotLocation> locations = parsed?.Locations?
                .Where(l => l.TerritoryId == territory && l.Position != null)
                .Select(l => new AcceptedCarrotLocation(
                    l.CandidateId,
                    (ushort)l.TerritoryId,
                    new Vector3(l.Position!.X, l.Position.Y, l.Position.Z)))
                .ToList()
                ?? [];

            Interlocked.Exchange(ref completedCatalog, CatalogOutcome.Ok(territory, locations));
        }
        catch (Exception ex)
        {
            Interlocked.Exchange(ref completedCatalog, CatalogOutcome.Failed(territory, ex.Message));
        }
    }

    private readonly record struct PendingSubmit(ushort TerritoryId, Vector3 Position, string Key);

    private sealed class UploadOutcome
    {
        public required string Key { get; init; }

        public required ushort TerritoryId { get; init; }

        public required float X { get; init; }

        public required float Y { get; init; }

        public required float Z { get; init; }

        public required bool Success { get; init; }

        public string? Status { get; init; }

        public string? Error { get; init; }

        public static UploadOutcome Ok(PendingSubmit pending) => new()
        {
            Key = pending.Key,
            TerritoryId = pending.TerritoryId,
            X = pending.Position.X,
            Y = pending.Position.Y,
            Z = pending.Position.Z,
            Success = true,
        };

        public static UploadOutcome Rejected(PendingSubmit pending, string status) => new()
        {
            Key = pending.Key,
            TerritoryId = pending.TerritoryId,
            X = pending.Position.X,
            Y = pending.Position.Y,
            Z = pending.Position.Z,
            Success = false,
            Status = status,
        };

        public static UploadOutcome Failed(PendingSubmit pending, string error) => new()
        {
            Key = pending.Key,
            TerritoryId = pending.TerritoryId,
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

        public IReadOnlyList<AcceptedCarrotLocation> Locations { get; init; } = [];

        public string? Status { get; init; }

        public string? Error { get; init; }

        public static CatalogOutcome Ok(ushort territory, IReadOnlyList<AcceptedCarrotLocation> locations) => new()
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
