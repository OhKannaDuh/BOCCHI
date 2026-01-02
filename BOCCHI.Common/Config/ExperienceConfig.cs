using Ocelot.Config;
using Ocelot.Config.Fields;

namespace BOCCHI.Common.Config;

[Serializable]
[ConfigGroup("trackers")]
public class ExperienceConfig : IAutoConfig
{
    [IntRange(1, 180)] public int TrackedDuration { get; set; } = 5;

    [IntRange(1, 60)] public int GraphBucketSize { get; set; } = 15;

    [Checkbox] public bool ResetOnZoneChange { get; set; } = true;
}
