using BOCCHI.Common.Config;
using BOCCHI.Common.Data.SupportJobs;
using BOCCHI.Experience.Data;
using FFXIVClientStructs.FFXIV.Client.Game.InstanceContent;
using Ocelot.Lifecycle;

namespace BOCCHI.Experience.Services;

public class ExperienceTracker(
    ExperienceConfig config,
    ISupportJobFactory supportJobs
) : IExperienceTracker, IOnUpdate
{
    private long TotalExperience = 0;

    private readonly List<DeltaSnapshot> snapshots = [];

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

    private long TrackedExperience
    {
        get => snapshots.Sum(s => s.Delta);
    }

    public double ExperiencePerHour
    {
        get
        {
            if (snapshots.Count == 0)
            {
                return 0;
            }

            var now = DateTime.UtcNow;
            var windowStart = now - Duration;

            var oldest = snapshots[0].Time;
            var start = oldest > windowStart ? oldest : windowStart;

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

            return TrackedExperience / hours;
        }
    }

    public unsafe void Update()
    {
        var state = PublicContentOccultCrescent.GetState();
        if (state == null)
        {
            return;
        }

        PruneOldSnapshots();

        var current = GetCurrentTotalExperience();

        if (TotalExperience <= 0)
        {
            TotalExperience = current;
            return;
        }

        var delta = current - TotalExperience;
        TotalExperience = current;

        if (delta <= 0)
        {
            return;
        }

        snapshots.Add(new DeltaSnapshot(delta, DateTime.UtcNow));
    }

    private void PruneOldSnapshots()
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

    private long GetCurrentTotalExperience()
    {
        return supportJobs.All().Sum(j => j.TotalExperience);
    }

    public float[] GetExperienceHistory(TimeSpan sampleDuration)
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
}
