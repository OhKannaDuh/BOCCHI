using BOCCHI.Common.Config;
using BOCCHI.Currency.Data;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.InstanceContent;
using Ocelot.Lifecycle;

namespace BOCCHI.Currency.Services;

public class CurrencyTracker(CurrencyConfig config) : ICurrencyTracker, IOnUpdate
{
    private long TotalGold = 0;

    private long TotalSilver = 0;

    private readonly List<DeltaSnapshot> goldSnapshots = [];

    private readonly List<DeltaSnapshot> silverSnapshots = [];

    public UpdateLimit UpdateLimit
    {
        get => new()
        {
            Mode = UpdateLimitMode.Milliseconds,
            Limit = 250,
        };
    }

    private TimeSpan Duration
    {
        get => TimeSpan.FromMinutes(config.TrackedDuration);
    }

    public double GoldPerHour
    {
        get => GetPerHourRate(goldSnapshots);
    }

    public double SilverPerHour
    {
        get => GetPerHourRate(silverSnapshots);
    }

    public float[] GetGoldHistory(TimeSpan sampleDuration)
    {
        return GetHistory(sampleDuration, goldSnapshots);
    }

    public float[] GetSilverHistory(TimeSpan sampleDuration)
    {
        return GetHistory(sampleDuration, silverSnapshots);
    }

    public unsafe void Update()
    {
        var state = PublicContentOccultCrescent.GetState();
        if (state == null)
        {
            return;
        }

        PruneOldSnapshots(goldSnapshots);
        PruneOldSnapshots(silverSnapshots);

        var gold = GetCurrentGold();
        var silver = GetCurrentSilver();

        if (TotalGold <= 0 && TotalSilver <= 0)
        {
            TotalGold = gold;
            TotalSilver = silver;
            return;
        }

        if (gold != TotalGold)
        {
            var delta = gold - TotalGold;
            TotalGold = gold;
            if (delta > 0)
            {
                goldSnapshots.Add(new DeltaSnapshot(delta, DateTime.UtcNow));
            }
        }

        if (silver != TotalSilver)
        {
            var delta = silver - TotalSilver;
            TotalSilver = silver;
            if (delta > 0)
            {
                silverSnapshots.Add(new DeltaSnapshot(delta, DateTime.UtcNow));
            }
        }
    }

    private void PruneOldSnapshots(List<DeltaSnapshot> snapshots)
    {
        if (snapshots.Count == 0)
        {
            return;
        }

        var cutoff = DateTime.UtcNow - Duration;

        var index = 0;
        while (index < snapshots.Count && snapshots[index].Time < cutoff)
        {
            index++;
        }

        if (index > 0)
        {
            snapshots.RemoveRange(0, index);
        }
    }

    private double GetPerHourRate(List<DeltaSnapshot> snapshots)
    {
        if (snapshots.Count == 0)
        {
            return 0;
        }

        var now = DateTime.UtcNow;
        var windowStart = now - Duration;

        var start = snapshots[0].Time > windowStart ? snapshots[0].Time : windowStart;

        var elapsed = now - start;

        if (elapsed < TimeSpan.FromSeconds(10))
        {
            return 0;
        }

        var hours = elapsed.TotalHours;
        if (hours <= 0)
        {
            return 0;
        }

        return snapshots.Sum(s => s.Delta) / hours;
    }

    private float[] GetHistory(TimeSpan sampleDuration, List<DeltaSnapshot> snapshots)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(sampleDuration, TimeSpan.Zero);

        var now = DateTime.UtcNow;
        var start = now - Duration;

        var relevant = snapshots
            .Where(s => s.Time >= start)
            .OrderBy(s => s.Time)
            .ToList();

        if (relevant.Count == 0)
        {
            return [];
        }

        var bucketCount = (int)Math.Floor(Duration.TotalSeconds / sampleDuration.TotalSeconds);
        if (bucketCount <= 0)
        {
            return [];
        }

        var bucketXp = new double[bucketCount];

        foreach (var s in relevant)
        {
            var secondsFromStart = (s.Time - start).TotalSeconds;
            var index = (int)(secondsFromStart / sampleDuration.TotalSeconds);

            if (index < 0 || index >= bucketCount)
            {
                continue;
            }

            bucketXp[index] += s.Delta;
        }

        var result = new float[bucketCount];
        var bucketSeconds = sampleDuration.TotalSeconds;

        for (var i = 0; i < bucketCount; i++)
        {
            var xp = bucketXp[i];

            if (xp <= 0)
            {
                result[i] = 0f;
                continue;
            }

            var xpPerHour = xp / bucketSeconds * 3600.0;
            result[i] = (float)xpPerHour;
        }

        return result;
    }

    private static unsafe int GetCurrentGold()
    {
        const int ITEM_ID = 45044;

        var im = InventoryManager.Instance();
        return im == null ? 0 : im->GetInventoryItemCount(ITEM_ID);
    }

    private static unsafe int GetCurrentSilver()
    {
        const int ITEM_ID = 45043;

        var im = InventoryManager.Instance();
        return im == null ? 0 : im->GetInventoryItemCount(ITEM_ID);
    }
}
