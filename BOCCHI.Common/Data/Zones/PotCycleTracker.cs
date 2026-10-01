using BOCCHI.Common.Data.Fates;
using BOCCHI.Common.Data.Zones.Graph;
using BOCCHI.Common.Services;
using Ocelot.Lifecycle;
using Ocelot.Services.Logger;

namespace BOCCHI.Common.Data.Zones;

public sealed record PotCycleSnapshot
{
    public ushort TerritoryTypeId { get; init; }

    public bool HasKnownAnchor { get; init; }

    public int AnchorPotFateId { get; init; }

    public DateTimeOffset AnchorSpawnAt { get; init; }

    public bool IsRemoteAnchor { get; init; }

    public int CurrentActivePotFateId { get; init; }

    public int PredictedNextPotFateId { get; init; }

    public DateTimeOffset PredictedNextSpawnAt { get; init; }

    public bool HasPredictedNextPot =>
        HasKnownAnchor
        && PredictedNextPotFateId != 0
        && PredictedNextSpawnAt > DateTimeOffset.MinValue;

    public bool HasAnythingToShow =>
        CurrentActivePotFateId != 0 || HasPredictedNextPot;
}

public readonly record struct PotFallbackStartDecision(bool AllowStart, string Reason);

public interface IPotCycleTracker
{
    PotCycleSnapshot Snapshot { get; }

    IReadOnlyList<PotCycleSnapshot> KnownCycles { get; }

    bool TryApplyRemoteAnchor(
        int potFateId,
        DateTimeOffset spawnAt,
        ushort territoryTypeId,
        bool overwriteExisting = false);

    void Invalidate(ushort territoryTypeId, string? reason = null);
}

public sealed class PotCycleTracker
(
    IFateRepository fates,
    IZoneProvider zones,
    ILogger<PotCycleTracker> logger
) : IPotCycleTracker, IOnUpdate
{
    private static readonly TimeSpan PotCycleInterval = TimeSpan.FromMinutes(30);

    public static readonly TimeSpan PredictionStaleGrace = TimeSpan.FromMinutes(5);

    private static readonly PotCycleSnapshot Empty = new();

    private readonly Dictionary<ushort, PotCycleSnapshot> cycles = new();

    private IReadOnlyList<PotCycleSnapshot> knownCyclesCache = [];

    private bool knownCyclesDirty = true;

    public PotCycleSnapshot Snapshot
    {
        get
        {
            IZone zone = zones.GetZone();
            if (!zone.IsOccultCrescentZone())
            {
                return Empty;
            }

            return cycles.TryGetValue(zone.TerritoryType, out PotCycleSnapshot? snap) ? snap : Empty;
        }
    }

    public IReadOnlyList<PotCycleSnapshot> KnownCycles
    {
        get
        {
            if (knownCyclesDirty)
            {
                knownCyclesCache = cycles.Values
                    .Where(c => c.HasAnythingToShow)
                    .OrderBy(c => c.TerritoryTypeId)
                    .ToList();
                knownCyclesDirty = false;
            }

            return knownCyclesCache;
        }
    }

    public UpdateLimit UpdateLimit =>
        new()
        {
            Mode = UpdateLimitMode.Milliseconds,
            Limit = 500
        };

    public void Update()
    {
        IZone zone = zones.GetZone();
        if (!zone.IsOccultCrescentZone())
        {
            return;
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        List<ActivityData> potFates = zone.GetPotFateData();
        if (potFates.Count == 0)
        {
            return;
        }

        ushort territory = zone.TerritoryType;
        Fate? active = fates.Snapshot().FirstOrDefault(f => potFates.Any(p => p.Id == f.Id.Value));
        PotCycleSnapshot previous = cycles.TryGetValue(territory, out PotCycleSnapshot? existing)
            ? existing
            : Empty;

        cycles[territory] = BuildSnapshot(territory, potFates, active, now, previous);
        knownCyclesDirty = true;
    }

    public void Invalidate(ushort territoryTypeId, string? reason = null)
    {
        if (!cycles.Remove(territoryTypeId))
        {
            return;
        }

        knownCyclesDirty = true;

        logger.Debug(
            "[PotCycleTracker] cleared schedule for territory {Territory} ({Reason})",
            territoryTypeId,
            string.IsNullOrEmpty(reason) ? "unspecified" : reason);
    }

    public bool TryApplyRemoteAnchor(
        int potFateId,
        DateTimeOffset spawnAt,
        ushort territoryTypeId,
        bool overwriteExisting = false)
    {
        IZone zone = zones.GetZone();
        if (!zone.IsOccultCrescentZone() || zone.TerritoryType != territoryTypeId)
        {
            return false;
        }

        List<ActivityData> potFates = zone.GetPotFateData();
        if (potFates.All(p => p.Id != potFateId))
        {
            return false;
        }

        PotCycleSnapshot previous = cycles.TryGetValue(territoryTypeId, out PotCycleSnapshot? existing)
            ? existing
            : Empty;

        if (previous.CurrentActivePotFateId != 0)
        {
            return false;
        }

        if (overwriteExisting)
        {
            if (previous.HasKnownAnchor
                && previous.AnchorPotFateId == potFateId
                && previous.AnchorSpawnAt.ToUnixTimeSeconds() == spawnAt.ToUnixTimeSeconds())
            {
                return false;
            }
        }
        else if (previous.HasKnownAnchor
                 && previous.AnchorSpawnAt != DateTimeOffset.MinValue
                 && previous.AnchorSpawnAt >= spawnAt)
        {
            return false;
        }

        ActivityData? opposite = potFates.FirstOrDefault(p => p.Id != potFateId);
        Fate? live = fates.Snapshot().FirstOrDefault(f => f.Id.Value == potFateId);
        DateTimeOffset nextSpawn = spawnAt + PotCycleInterval;

        PotCycleSnapshot applied = new()
        {
            TerritoryTypeId = territoryTypeId,
            HasKnownAnchor = true,
            AnchorPotFateId = potFateId,
            AnchorSpawnAt = spawnAt,
            IsRemoteAnchor = true,
            CurrentActivePotFateId = live != null ? potFateId : 0,
            PredictedNextPotFateId = opposite?.Id ?? 0,
            PredictedNextSpawnAt = opposite == null ? DateTimeOffset.MinValue : nextSpawn,
        };

        cycles[territoryTypeId] = applied;
        knownCyclesDirty = true;
        logger.Debug(
            overwriteExisting && previous.HasKnownAnchor
                ? $"[PotCycleTracker] linker overwrite zone={territoryTypeId} pot={potFateId} spawnAt={spawnAt:O} (was pot={previous.AnchorPotFateId} spawnAt={previous.AnchorSpawnAt:O})"
                : $"[PotCycleTracker] remote anchor zone={territoryTypeId} pot={potFateId} spawnAt={spawnAt:O} next={opposite?.Id ?? 0} nextSpawnAt={nextSpawn:O}");
        return true;
    }

    private PotCycleSnapshot BuildSnapshot(
        ushort territoryType,
        List<ActivityData> potFates,
        Fate? active,
        DateTimeOffset now,
        PotCycleSnapshot previous)
    {
        if (active == null)
        {
            return RollIdlePrediction(territoryType, potFates, now, previous);
        }

        int activeId = active.Id.Value;
        if (previous.TerritoryTypeId == territoryType
            && previous.CurrentActivePotFateId == activeId
            && !previous.IsRemoteAnchor)
        {
            return previous;
        }

        DateTimeOffset spawnAt = active.StartTimeEpoch > 0
            ? DateTimeOffset.FromUnixTimeSeconds(active.StartTimeEpoch)
            : now;
        ActivityData? opposite = potFates.FirstOrDefault(p => p.Id != activeId);
        DateTimeOffset nextSpawn = spawnAt + PotCycleInterval;

        logger.Debug(
            $"[PotCycleTracker] zone={territoryType} local anchor pot={activeId} next={opposite?.Id ?? 0} nextSpawnAt={(opposite == null ? "none" : nextSpawn.ToString("O"))}");

        return new PotCycleSnapshot
        {
            TerritoryTypeId = territoryType,
            HasKnownAnchor = true,
            AnchorPotFateId = activeId,
            AnchorSpawnAt = spawnAt,
            IsRemoteAnchor = false,
            CurrentActivePotFateId = activeId,
            PredictedNextPotFateId = opposite?.Id ?? 0,
            PredictedNextSpawnAt = opposite == null ? DateTimeOffset.MinValue : nextSpawn,
        };
    }

    private PotCycleSnapshot RollIdlePrediction(
        ushort territoryType,
        List<ActivityData> potFates,
        DateTimeOffset now,
        PotCycleSnapshot previous)
    {
        if (!previous.HasPredictedNextPot)
        {
            return previous with
            {
                TerritoryTypeId = territoryType,
                CurrentActivePotFateId = 0,
            };
        }

        DateTimeOffset nextSpawn = previous.PredictedNextSpawnAt;
        if (now <= nextSpawn + PredictionStaleGrace)
        {
            return previous with
            {
                TerritoryTypeId = territoryType,
                CurrentActivePotFateId = 0,
            };
        }

        int nextPotId = previous.PredictedNextPotFateId;
        int anchorId = previous.AnchorPotFateId;
        DateTimeOffset anchorSpawn = previous.AnchorSpawnAt;
        int rolled = 0;

        while (now > nextSpawn + PredictionStaleGrace && nextPotId != 0)
        {
            anchorId = nextPotId;
            anchorSpawn = nextSpawn;
            ActivityData? opposite = potFates.FirstOrDefault(p => p.Id != nextPotId);
            nextPotId = opposite?.Id ?? 0;
            nextSpawn += PotCycleInterval;
            rolled++;
        }

        if (rolled > 0)
        {
            logger.Debug(
                "[PotCycleTracker] zone={Territory} rolled stale prediction x{Rolled} → next={NextId} at {NextSpawn:O}",
                territoryType,
                rolled,
                nextPotId,
                nextSpawn);
        }

        return new PotCycleSnapshot
        {
            TerritoryTypeId = territoryType,
            HasKnownAnchor = nextPotId != 0,
            AnchorPotFateId = anchorId,
            AnchorSpawnAt = anchorSpawn,
            IsRemoteAnchor = previous.IsRemoteAnchor,
            CurrentActivePotFateId = 0,
            PredictedNextPotFateId = nextPotId,
            PredictedNextSpawnAt = nextPotId == 0 ? DateTimeOffset.MinValue : nextSpawn,
        };
    }
}

public static class PotFallbackWindow
{
    public static PotFallbackStartDecision Evaluate(
        PotCycleSnapshot cycle,
        DateTimeOffset now,
        TimeSpan cutoffWindow,
        bool potFarmingEnabled,
        string activityName)
    {
        if (!potFarmingEnabled)
        {
            return new(true, $"{activityName} allowed: pot farming disabled.");
        }

        if (!cycle.HasPredictedNextPot)
        {
            return new(true, $"{activityName} allowed: no pot spawn predicted yet.");
        }

        TimeSpan timeUntilSpawn = cycle.PredictedNextSpawnAt - now;

        if (now > cycle.PredictedNextSpawnAt + PotCycleTracker.PredictionStaleGrace)
        {
            return new(true, $"{activityName} allowed: pot prediction overdue (stale).");
        }

        // Cutoff is minutes until the pot spawns — independent of spawn-lead (leave-early).
        // Measuring against departure (= spawn − lead) stacked the sliders and blocked FATEs
        // for lead+cutoff minutes (e.g. lead 5 + FATE 5 ⇒ no FATEs for 10m).
        if (cutoffWindow > TimeSpan.Zero && timeUntilSpawn <= cutoffWindow)
        {
            return new(
                false,
                $"{activityName} blocked: next pot in {Format(timeUntilSpawn)} (cutoff {cutoffWindow.TotalMinutes:0}m).");
        }

        return new(true, $"{activityName} allowed: next pot in {Format(timeUntilSpawn)}.");
    }

    public static bool ShouldPreposition(
        PotCycleSnapshot cycle,
        DateTimeOffset now,
        int spawnLeadMinutes,
        bool potFarmingEnabled)
    {
        if (!potFarmingEnabled || !cycle.HasPredictedNextPot)
        {
            return false;
        }

        if (now > cycle.PredictedNextSpawnAt + PotCycleTracker.PredictionStaleGrace)
        {
            return false;
        }

        TimeSpan timeUntilSpawn = cycle.PredictedNextSpawnAt - now;
        return timeUntilSpawn <= TimeSpan.FromMinutes(Math.Max(0, spawnLeadMinutes));
    }

    private static string Format(TimeSpan value)
    {
        if (value <= TimeSpan.Zero)
        {
            return "0m";
        }

        return value.TotalMinutes >= 1
            ? $"{Math.Floor(value.TotalMinutes):0}m"
            : $"{Math.Ceiling(value.TotalSeconds):0}s";
    }
}
