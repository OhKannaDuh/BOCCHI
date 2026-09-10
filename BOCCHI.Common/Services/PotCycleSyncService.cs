using BOCCHI.Common.Config;
using BOCCHI.Common.Data.Fates;
using BOCCHI.Common.Data.Zones;
using Dalamud.Plugin;
using Ocelot.Lifecycle;
using Ocelot.Services.Logger;
using Ocelot.Services.PlayerState;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BOCCHI.Common.Services;

/// <summary>Sync pot-cycle anchors with the BOCCHI Worker when shared maps are enabled.</summary>
public sealed class PotCycleSyncService
(
    TreasureConfig config,
    IZoneProvider zones,
    IPotCycleTracker potCycles,
    IFateRepository fates,
    IPlayer player,
    IDalamudPluginInterface plugin,
    ILogger<PotCycleSyncService> logger
) : IOnUpdate
{
    public const string ApiBaseUrl = "https://bocchi-coffer-api.kagekazu.workers.dev";

    public const string ApiUrl = ApiBaseUrl + "/api/v1/pot-cycles";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static readonly TimeSpan RetryDelay = TimeSpan.FromMinutes(1);

    private static readonly TimeSpan FetchRetryDelay = TimeSpan.FromSeconds(20);

    private static readonly TimeSpan FetchMissRetryWindow = TimeSpan.FromMinutes(3);

    private static readonly TimeSpan FetchRateLimitMinDelay = TimeSpan.FromMinutes(2);

    private static readonly TimeSpan FetchRateLimitMaxDelay = TimeSpan.FromMinutes(10);

    private ushort fingerprintTerritory;

    private string? instanceKey;

    private uint fingerprintFateId;

    private int fingerprintStartEpoch;

    private ushort lastUploadedTerritory;

    private int lastUploadedPotFateId;

    private long lastUploadedSpawnUnix;

    private string? lastFetchedInstanceKey;

    private DateTime fetchMissRetryUntilUtc = DateTime.MinValue;

    private DateTime nextUploadAttemptUtc = DateTime.MinValue;

    private DateTime nextFetchAttemptUtc = DateTime.MinValue;

    private TimeSpan fetchRateLimitDelay = FetchRateLimitMinDelay;

    private bool loggedFetchRateLimit;

    private bool uploadInFlight;

    private bool fetchInFlight;

    private UploadOutcome? completedUpload;

    private FetchOutcome? completedFetch;

    public UpdateLimit UpdateLimit =>
        new()
        {
            Mode = UpdateLimitMode.Milliseconds,
            Limit = 1000
        };

    public void Update()
    {
        ApplyCompletedWork();

        if (!config.EnableSharedMaps)
        {
            ResetSession();
            return;
        }

        IZone zone = zones.GetZone();
        if (!zone.IsOccultCrescentZone())
        {
            ResetSession();
            return;
        }

        ushort territory = zone.TerritoryType;
        if (fingerprintTerritory != territory)
        {
            // Keep the other zone's pot timer (SH/NH are tracked separately).
            ResetFingerprint(territory);
        }

        RefreshFingerprint(territory);
        if (string.IsNullOrEmpty(instanceKey))
        {
            return;
        }

        PotCycleSnapshot snap = potCycles.Snapshot;
        StartUpload(snap, territory);
        StartFetch(snap, territory);
    }

    private void ApplyCompletedWork()
    {
        UploadOutcome? upload = Interlocked.Exchange(ref completedUpload, null);
        if (upload != null)
        {
            uploadInFlight = false;
            if (upload.Success)
            {
                lastUploadedTerritory = upload.TerritoryId;
                lastUploadedPotFateId = upload.PotFateId;
                lastUploadedSpawnUnix = upload.SpawnUnix;
                nextUploadAttemptUtc = DateTime.UtcNow;
                logger.Debug(
                    "[PotCycleSync] uploaded pot={PotId} spawn={Spawn} key={KeyPrefix}…",
                    upload.PotFateId,
                    upload.SpawnUnix,
                    upload.KeyPrefix);
            }
            else
            {
                nextUploadAttemptUtc = DateTime.UtcNow + RetryDelay;
                if (upload.Error is { } uploadError)
                {
                    logger.Warn("[PotCycleSync] upload failed: {Message}", uploadError);
                }
                else
                {
                    logger.Warn("[PotCycleSync] upload rejected: {Status}", upload.Status ?? "?");
                }
            }
        }

        FetchOutcome? fetch = Interlocked.Exchange(ref completedFetch, null);
        if (fetch == null)
        {
            return;
        }

        fetchInFlight = false;
        if (!fetch.Success)
        {
            bool rateLimited = IsRateLimited(fetch.Status);
            if (rateLimited)
            {
                nextFetchAttemptUtc = DateTime.UtcNow + fetchRateLimitDelay;
                if (!loggedFetchRateLimit)
                {
                    loggedFetchRateLimit = true;
                    logger.Warn(
                        "[PotCycleSync] fetch rate-limited — retrying in {Minutes:0}m",
                        fetchRateLimitDelay.TotalMinutes);
                }
                else
                {
                    logger.Debug(
                        "[PotCycleSync] fetch rejected: {Status} — next try in {Minutes:0}m",
                        fetch.Status ?? "?",
                        fetchRateLimitDelay.TotalMinutes);
                }

                fetchRateLimitDelay = TimeSpan.FromMinutes(
                    Math.Min(FetchRateLimitMaxDelay.TotalMinutes, fetchRateLimitDelay.TotalMinutes * 2));
            }
            else
            {
                nextFetchAttemptUtc = DateTime.UtcNow + FetchRetryDelay;
                if (fetch.Error is { } fetchError)
                {
                    logger.Warn("[PotCycleSync] fetch failed: {Message}", fetchError);
                }
                else
                {
                    logger.Warn("[PotCycleSync] fetch rejected: {Status}", fetch.Status ?? "?");
                }
            }

            return;
        }

        fetchRateLimitDelay = FetchRateLimitMinDelay;
        loggedFetchRateLimit = false;

        if (!fetch.Found || fetch.PotFateId == 0 || fetch.SpawnUnix <= 0)
        {
            if (fetchMissRetryUntilUtc == DateTime.MinValue)
            {
                fetchMissRetryUntilUtc = DateTime.UtcNow + FetchMissRetryWindow;
            }

            if (DateTime.UtcNow >= fetchMissRetryUntilUtc)
            {
                lastFetchedInstanceKey = fetch.InstanceKey;
                logger.Debug(
                    "[PotCycleSync] fetch miss — no cycle for key {Key}… (stopped retrying)",
                    Shorten(fetch.InstanceKey));
                return;
            }

            nextFetchAttemptUtc = DateTime.UtcNow + FetchRetryDelay;
            logger.Debug(
                "[PotCycleSync] fetch miss — no cycle for key {Key}… retrying",
                Shorten(fetch.InstanceKey));
            return;
        }

        lastFetchedInstanceKey = fetch.InstanceKey;
        fetchMissRetryUntilUtc = DateTime.MinValue;
        nextFetchAttemptUtc = DateTime.UtcNow;

        if (fetch.ResponseTerritoryId != 0 && fetch.ResponseTerritoryId != fetch.RequestTerritoryId)
        {
            logger.Warn(
                "[PotCycleSync] fetch hit discarded — territory {Response} does not match {Request}",
                fetch.ResponseTerritoryId,
                fetch.RequestTerritoryId);
            return;
        }

        DateTimeOffset spawnAt = DateTimeOffset.FromUnixTimeSeconds(fetch.SpawnUnix);
        if (potCycles.TryApplyRemoteAnchor(fetch.PotFateId, spawnAt, fetch.RequestTerritoryId))
        {
            logger.Debug(
                "[PotCycleSync] fetch hit — applied remote pot={PotId} spawn={Spawn}",
                fetch.PotFateId,
                fetch.SpawnUnix);
            return;
        }

        logger.Debug(
            "[PotCycleSync] fetch hit but not applied — pot={PotId} spawn={Spawn} rejected by the "
            + "local tracker (usually a local anchor arrived first)",
            fetch.PotFateId,
            fetch.SpawnUnix);
    }

    private static bool IsRateLimited(string? status) =>
        status is "TooManyRequests" or "429";

    private static string Shorten(string? key) =>
        key is { Length: >= 8 } ? key[..8] : key ?? "?";

    private void StartUpload(PotCycleSnapshot snap, ushort territory)
    {
        if (uploadInFlight
            || snap.TerritoryTypeId != territory
            || !snap.HasKnownAnchor
            || snap.IsRemoteAnchor
            || snap.AnchorPotFateId == 0
            || snap.AnchorSpawnAt == DateTimeOffset.MinValue)
        {
            return;
        }

        if (DateTime.UtcNow < nextUploadAttemptUtc || instanceKey == null)
        {
            return;
        }

        long spawnUnix = snap.AnchorSpawnAt.ToUnixTimeSeconds();
        // Skip re-upload when only the FATE fingerprint rotated.
        if (lastUploadedTerritory == territory
            && lastUploadedPotFateId == snap.AnchorPotFateId
            && lastUploadedSpawnUnix == spawnUnix)
        {
            return;
        }

        uint? datacenterId = TryGetDatacenterId();
        if (datacenterId is not uint dc)
        {
            return;
        }

        string key = instanceKey;
        string json = JsonSerializer.Serialize(new
        {
            instanceKey = key,
            territoryId = (int)territory,
            datacenterId = (int)dc,
            potFateId = snap.AnchorPotFateId,
            spawnAtUnix = spawnUnix,
            installationHash = InstallationId.GetHash(plugin),
            pluginVersion = typeof(PotCycleSyncService).Assembly.GetName().Version?.ToString() ?? "0",
            observedAtUtc = DateTime.UtcNow.ToString("O"),
        });

        uploadInFlight = true;
        _ = UploadAsync(territory, snap.AnchorPotFateId, spawnUnix, key, json);
    }

    private async Task UploadAsync(
        ushort territory,
        int potFateId,
        long spawnUnix,
        string instanceKeyValue,
        string json)
    {
        try
        {
            using HttpRequestMessage request = new(HttpMethod.Post, ApiUrl)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };

            using HttpResponseMessage response = await Http.SendAsync(request).ConfigureAwait(false);
            Interlocked.Exchange(
                ref completedUpload,
                response.IsSuccessStatusCode
                    ? UploadOutcome.Ok(territory, potFateId, spawnUnix, instanceKeyValue)
                    : UploadOutcome.Rejected(response.StatusCode.ToString()));
        }
        catch (Exception ex)
        {
            Interlocked.Exchange(ref completedUpload, UploadOutcome.Failed(ex.Message));
        }
    }

    private void StartFetch(PotCycleSnapshot snap, ushort territory)
    {
        if (fetchInFlight || snap.HasKnownAnchor || instanceKey == null)
        {
            return;
        }

        if (lastFetchedInstanceKey == instanceKey)
        {
            return;
        }

        if (DateTime.UtcNow < nextFetchAttemptUtc)
        {
            return;
        }

        string key = instanceKey;
        fetchInFlight = true;
        _ = FetchAsync(key, territory);
    }

    private async Task FetchAsync(string instanceKeyValue, ushort territory)
    {
        try
        {
            string url = $"{ApiUrl}?instanceKey={Uri.EscapeDataString(instanceKeyValue)}";
            using HttpRequestMessage request = new(HttpMethod.Get, url);
            using HttpResponseMessage response = await Http.SendAsync(request).ConfigureAwait(false);
            string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                Interlocked.Exchange(
                    ref completedFetch,
                    FetchOutcome.Rejected(response.StatusCode.ToString()));
                return;
            }

            PotCycleApiResponse? parsed = JsonSerializer.Deserialize<PotCycleApiResponse>(body, JsonOptions);
            Interlocked.Exchange(
                ref completedFetch,
                FetchOutcome.Ok(
                    instanceKeyValue,
                    territory,
                    parsed is { Found: true },
                    parsed?.TerritoryId ?? 0,
                    parsed?.PotFateId ?? 0,
                    parsed?.SpawnAtUnix ?? 0));
        }
        catch (Exception ex)
        {
            Interlocked.Exchange(ref completedFetch, FetchOutcome.Failed(ex.Message));
        }
    }

    private void RefreshFingerprint(ushort territory)
    {
        uint? datacenterId = TryGetDatacenterId();
        if (datacenterId is not uint dc)
        {
            return;
        }

        // Hold fingerprint until that FATE ends (avoid timer wipe).
        if (instanceKey != null
            && fingerprintFateId != 0
            && fates.Snapshot().Any(f =>
                f.Id.Value == fingerprintFateId && f.StartTimeEpoch == fingerprintStartEpoch))
        {
            return;
        }

        Fate? fingerprintFate = fates.Snapshot()
            .Where(f => f.StartTimeEpoch > 0)
            .OrderBy(f => f.StartTimeEpoch)
            .ThenBy(f => f.Id.Value)
            .FirstOrDefault();

        if (fingerprintFate == null)
        {
            return;
        }

        string newKey = ComputeInstanceKey(dc, fingerprintFate.Id.Value, fingerprintFate.StartTimeEpoch);
        if (instanceKey == newKey)
        {
            fingerprintFateId = fingerprintFate.Id.Value;
            fingerprintStartEpoch = fingerprintFate.StartTimeEpoch;
            return;
        }

        bool firstKey = instanceKey == null;
        fingerprintFateId = fingerprintFate.Id.Value;
        fingerprintStartEpoch = fingerprintFate.StartTimeEpoch;
        instanceKey = newKey;
        fingerprintTerritory = territory;
        lastFetchedInstanceKey = null;
        fetchMissRetryUntilUtc = DateTime.MinValue;

        // Do not clear the pot timer on FATE-roster churn. Local/remote anchors re-validate
        // when the next pot is seen; wiping here caused "next pot → unknown".
        logger.Debug(
            firstKey
                ? "[PotCycleSync] instance key from fate={FateId} epoch={Epoch} key={KeyPrefix}…"
                : "[PotCycleSync] fingerprint fate ended — new key from fate={FateId} epoch={Epoch} key={KeyPrefix}… (pot timer kept)",
            fingerprintFateId,
            fingerprintStartEpoch,
            instanceKey[..8]);
    }

    private uint? TryGetDatacenterId()
    {
        try
        {
            return player.PlayerCharacter?.CurrentWorld.Value.DataCenter.RowId;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Linker-compatible: SHA-256 hex of three little-endian int32s (dc, fateId, startEpoch).</summary>
    private static string ComputeInstanceKey(uint datacenterId, uint fateId, int startTimeEpoch)
    {
        Span<byte> buffer = stackalloc byte[12];
        BitConverter.TryWriteBytes(buffer[..4], (int)datacenterId);
        BitConverter.TryWriteBytes(buffer[4..8], (int)fateId);
        BitConverter.TryWriteBytes(buffer[8..12], startTimeEpoch);
        return Convert.ToHexString(SHA256.HashData(buffer));
    }

    private void ResetFingerprint(ushort territory)
    {
        fingerprintTerritory = territory;
        instanceKey = null;
        fingerprintFateId = 0;
        fingerprintStartEpoch = 0;
        lastFetchedInstanceKey = null;
        fetchMissRetryUntilUtc = DateTime.MinValue;
        nextFetchAttemptUtc = DateTime.MinValue;
        fetchRateLimitDelay = FetchRateLimitMinDelay;
        loggedFetchRateLimit = false;

        // Entering a zone (or returning after leave) must not keep a previous instance's pot clock.
        // Stale HasKnownAnchor blocks sync fetch and makes Illegal Mode leave for pots too early/late.
        potCycles.Invalidate(territory, "new island/instance fingerprint");
    }

    private void ResetSession()
    {
        if (fingerprintTerritory == 0 && instanceKey == null)
        {
            return;
        }

        // Drop sync fingerprint. Territory schedules stay until that zone is entered again
        // (ResetFingerprint invalidates the destination so a new instance can sync).
        fingerprintTerritory = 0;
        instanceKey = null;
        fingerprintFateId = 0;
        fingerprintStartEpoch = 0;
        lastFetchedInstanceKey = null;
        fetchMissRetryUntilUtc = DateTime.MinValue;
        nextFetchAttemptUtc = DateTime.MinValue;
        fetchRateLimitDelay = FetchRateLimitMinDelay;
        loggedFetchRateLimit = false;
    }

    private sealed class UploadOutcome
    {
        public required bool Success { get; init; }

        public ushort TerritoryId { get; init; }

        public int PotFateId { get; init; }

        public long SpawnUnix { get; init; }

        public string InstanceKey { get; init; } = "";

        public string KeyPrefix { get; init; } = "";

        public string? Status { get; init; }

        public string? Error { get; init; }

        public static UploadOutcome Ok(ushort territory, int potFateId, long spawnUnix, string instanceKey) => new()
        {
            Success = true,
            TerritoryId = territory,
            PotFateId = potFateId,
            SpawnUnix = spawnUnix,
            InstanceKey = instanceKey,
            KeyPrefix = instanceKey.Length >= 8 ? instanceKey[..8] : instanceKey,
        };

        public static UploadOutcome Rejected(string status) => new()
        {
            Success = false,
            Status = status,
        };

        public static UploadOutcome Failed(string error) => new()
        {
            Success = false,
            Error = error,
        };
    }

    private sealed class FetchOutcome
    {
        public required bool Success { get; init; }

        public string InstanceKey { get; init; } = "";

        public ushort RequestTerritoryId { get; init; }

        public bool Found { get; init; }

        public int ResponseTerritoryId { get; init; }

        public int PotFateId { get; init; }

        public long SpawnUnix { get; init; }

        public string? Status { get; init; }

        public string? Error { get; init; }

        public static FetchOutcome Ok(
            string instanceKey,
            ushort requestTerritoryId,
            bool found,
            int responseTerritoryId,
            int potFateId,
            long spawnUnix) => new()
        {
            Success = true,
            InstanceKey = instanceKey,
            RequestTerritoryId = requestTerritoryId,
            Found = found,
            ResponseTerritoryId = responseTerritoryId,
            PotFateId = potFateId,
            SpawnUnix = spawnUnix,
        };

        public static FetchOutcome Rejected(string status) => new()
        {
            Success = false,
            Status = status,
        };

        public static FetchOutcome Failed(string error) => new()
        {
            Success = false,
            Error = error,
        };
    }

    private sealed class PotCycleApiResponse
    {
        [JsonPropertyName("found")]
        public bool Found { get; set; }

        [JsonPropertyName("territoryId")]
        public int TerritoryId { get; set; }

        [JsonPropertyName("potFateId")]
        public int PotFateId { get; set; }

        [JsonPropertyName("spawnAtUnix")]
        public long SpawnAtUnix { get; set; }
    }
}
