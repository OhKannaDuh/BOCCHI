using BOCCHI.Common.Config;
using BOCCHI.Common.Data.Zones;
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

public readonly record struct CrowdsourcedCofferCandidate(
    int CandidateId,
    ushort TerritoryId,
    uint DataId,
    Vector3 Position);

/// <summary>
///     Fetches accepted coffer candidates for Treasure Hunt and anonymously uploads opens
///     when shared maps are enabled. HTTP runs off the framework thread.
/// </summary>
public sealed class CofferLocationSyncService
(
    TreasureConfig config,
    IZoneProvider zones,
    IDalamudPluginInterface plugin,
    ILogger<CofferLocationSyncService> logger
) : IOnUpdate
{
    public const string ApiBaseUrl = PotCycleSyncService.ApiBaseUrl;

    public const string ObservationsUrl = ApiBaseUrl + "/api/v1/observations";

    public const string CandidatesUrl = ApiBaseUrl + "/api/v1/candidates";

    /// <summary>Match layout coffers to crowdsourced centroids (API cluster radius is 1.5).</summary>
    public const float MatchRadius = 3.5f;

    public const float MatchRadiusSq = MatchRadius * MatchRadius;

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

    private IReadOnlyList<CrowdsourcedCofferCandidate> accepted = [];

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

    public bool MatchesAccepted(Vector3 position)
    {
        IReadOnlyList<CrowdsourcedCofferCandidate> spots = GetAcceptedForCurrentZone();
        return spots.Any(c => Vector3.DistanceSquared(c.Position, position) <= MatchRadiusSq);
    }

    /// <summary>Kick a refresh before planning a hunt (non-blocking if already recent).</summary>
    public void EnsureFreshForHunt()
    {
        if (!config.EnableSharedMaps || !zones.GetZone().IsOccultCrescentZone())
        {
            return;
        }

        StartCatalogRefresh(zones.GetZone().TerritoryType, force: true);
    }

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

        queue.Enqueue(new PendingSubmit(territory, dataId, position, cofferType, key));
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
                    "[CofferLocationSync] uploaded dataId={DataId} pos=({X:F2},{Y:F2},{Z:F2})",
                    upload.DataId,
                    upload.X,
                    upload.Y,
                    upload.Z);
            }
            else
            {
                nextUploadAttemptUtc = DateTime.UtcNow + CrowdsourceSyncHttp.RetryDelay;
                if (upload.Error is { } uploadError)
                {
                    logger.Warn("[CofferLocationSync] upload failed: {Message}", uploadError);
                }
                else
                {
                    logger.Warn("[CofferLocationSync] upload rejected: {Status}", upload.Status ?? "?");
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
                "[CofferLocationSync] catalog territory={Territory} candidates={Count}",
                catalog.TerritoryId,
                accepted.Count);
        }
        else
        {
            nextCatalogFetchUtc = DateTime.UtcNow + CrowdsourceSyncHttp.RetryDelay;
            if (catalog.Error is { } catalogError)
            {
                logger.Warn("[CofferLocationSync] catalog failed: {Message}", catalogError);
            }
            else
            {
                logger.Warn("[CofferLocationSync] catalog rejected: {Status}", catalog.Status ?? "?");
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
            dataId = pending.DataId,
            worldX = pending.Position.X,
            worldY = pending.Position.Y,
            worldZ = pending.Position.Z,
            cofferType = pending.CofferType,
            installationHash = InstallationId.GetHash(plugin),
            pluginVersion = typeof(CofferLocationSyncService).Assembly.GetName().Version?.ToString() ?? "0",
            observedAtUtc = DateTime.UtcNow.ToString("O"),
        });

        uploadInFlight = true;
        _ = UploadAsync(pending, json);
    }

    private async Task UploadAsync(PendingSubmit pending, string json)
    {
        try
        {
            using HttpRequestMessage request = new(HttpMethod.Post, ObservationsUrl)
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
            string url = $"{CandidatesUrl}?territoryId={territory}";
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

            Interlocked.Exchange(ref completedCatalog, CatalogOutcome.Ok(territory, locations));
        }
        catch (Exception ex)
        {
            Interlocked.Exchange(ref completedCatalog, CatalogOutcome.Failed(territory, ex.Message));
        }
    }

    private readonly record struct PendingSubmit(
        ushort TerritoryId,
        uint DataId,
        Vector3 Position,
        string CofferType,
        string Key);

    private sealed class UploadOutcome
    {
        public required string Key { get; init; }

        public required uint DataId { get; init; }

        public required float X { get; init; }

        public required float Y { get; init; }

        public required float Z { get; init; }

        public required bool Success { get; init; }

        public string? Status { get; init; }

        public string? Error { get; init; }

        public static UploadOutcome Ok(PendingSubmit pending) => new()
        {
            Key = pending.Key,
            DataId = pending.DataId,
            X = pending.Position.X,
            Y = pending.Position.Y,
            Z = pending.Position.Z,
            Success = true,
        };

        public static UploadOutcome Rejected(PendingSubmit pending, string status) => new()
        {
            Key = pending.Key,
            DataId = pending.DataId,
            X = pending.Position.X,
            Y = pending.Position.Y,
            Z = pending.Position.Z,
            Success = false,
            Status = status,
        };

        public static UploadOutcome Failed(PendingSubmit pending, string error) => new()
        {
            Key = pending.Key,
            DataId = pending.DataId,
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

        public IReadOnlyList<CrowdsourcedCofferCandidate> Locations { get; init; } = [];

        public string? Status { get; init; }

        public string? Error { get; init; }

        public static CatalogOutcome Ok(
            ushort territory,
            IReadOnlyList<CrowdsourcedCofferCandidate> locations) => new()
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
