using System.Net.Http;
using System.Numerics;
using System.Text;

namespace BOCCHI.Treasure.Services;

public abstract class CrowdsourceLocationSync<TLocation>
{
    protected readonly Queue<PendingSubmit> queue = new();

    protected readonly HashSet<string> queuedKeys = new(StringComparer.Ordinal);

    protected readonly HashSet<string> submittedKeys = new(StringComparer.Ordinal);

    private DateTime nextUploadAttemptUtc = DateTime.MinValue;

    private DateTime nextCatalogFetchUtc = DateTime.MinValue;

    protected ushort catalogTerritory;

    private bool uploadInFlight;

    private bool catalogInFlight;

    private UploadOutcome? completedUpload;

    private CatalogOutcome? completedCatalog;

    protected IReadOnlyList<TLocation> accepted = [];

    protected abstract string UploadUrl { get; }
    protected abstract string CatalogUrl { get; }
    protected abstract string BuildPayload(PendingSubmit pending);
    protected abstract IReadOnlyList<TLocation> ParseCatalog(string body, ushort territory);
    protected abstract void LogUploaded(PendingSubmit pending);
    protected abstract void LogCatalog(ushort territory, int count);
    protected abstract void LogWarning(string message, string value);

    protected void ApplyCompletedWork()
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
                LogUploaded(upload.Pending);
            }
            else
            {
                if (CrowdsourceSyncHttp.IsPermanentRejection(upload.Status)
                    && queue.Count > 0
                    && queue.Peek().Key == upload.Key)
                {
                    PendingSubmit dropped = queue.Dequeue();
                    queuedKeys.Remove(dropped.Key);
                    submittedKeys.Add(dropped.Key);
                    LogWarning("server refused a report ({Status}); dropped it", upload.Status ?? "?");
                }

                nextUploadAttemptUtc = DateTime.UtcNow + CrowdsourceSyncHttp.RetryDelay;
                if (upload.Error is { } uploadError)
                {
                    LogWarning("upload failed: {Message}", uploadError);
                }
                else
                {
                    LogWarning("upload rejected: {Status}", upload.Status ?? "?");
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
            LogCatalog(catalog.TerritoryId, accepted.Count);
        }
        else
        {
            nextCatalogFetchUtc = DateTime.UtcNow + CrowdsourceSyncHttp.RetryDelay;
            if (catalog.Error is { } catalogError)
            {
                LogWarning("catalog failed: {Message}", catalogError);
            }
            else
            {
                LogWarning("catalog rejected: {Status}", catalog.Status ?? "?");
            }
        }
    }

    protected void StartNextUpload()
    {
        if (uploadInFlight || queue.Count == 0 || DateTime.UtcNow < nextUploadAttemptUtc)
        {
            return;
        }

        PendingSubmit pending = queue.Peek();
        string json = BuildPayload(pending);

        uploadInFlight = true;
        _ = UploadAsync(pending, json);
    }

    private async Task UploadAsync(PendingSubmit pending, string json)
    {
        try
        {
            using HttpRequestMessage request = new(HttpMethod.Post, UploadUrl)
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

    protected void StartCatalogRefresh(ushort territory, bool force = false)
    {
        if (catalogInFlight)
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
            string url = $"{CatalogUrl}?territoryId={territory}";
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

            IReadOnlyList<TLocation> locations = ParseCatalog(body, territory);

            Interlocked.Exchange(ref completedCatalog, CatalogOutcome.Ok(territory, locations));
        }
        catch (Exception ex)
        {
            Interlocked.Exchange(ref completedCatalog, CatalogOutcome.Failed(territory, ex.Message));
        }
    }

    protected readonly record struct PendingSubmit(
        ushort TerritoryId,
        Vector3 Position,
        string Key,
        uint DataId = 0,
        string CofferType = "",
        int PotFateId = 0,
        bool IsReroll = false);

    private sealed class UploadOutcome
    {
        public required PendingSubmit Pending { get; init; }
        public string Key => Pending.Key;
        public required bool Success { get; init; }
        public string? Status { get; init; }
        public string? Error { get; init; }
        public static UploadOutcome Ok(PendingSubmit pending) => new()
        {
            Pending = pending,
            Success = true,
        };
        public static UploadOutcome Rejected(PendingSubmit pending, string status) => new()
        {
            Pending = pending,
            Success = false,
            Status = status,
        };
        public static UploadOutcome Failed(PendingSubmit pending, string error) => new()
        {
            Pending = pending,
            Success = false,
            Error = error,
        };
    }

    private sealed class CatalogOutcome
    {
        public required ushort TerritoryId { get; init; }

        public required bool Success { get; init; }

        public IReadOnlyList<TLocation> Locations { get; init; } = [];

        public string? Status { get; init; }

        public string? Error { get; init; }

        public static CatalogOutcome Ok(
            ushort territory,
            IReadOnlyList<TLocation> locations) => new()
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

}
