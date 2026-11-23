using Newtonsoft.Json;
using BOCCHI.Common.Config.Fields;
using Ocelot.Config;
using Ocelot.Config.Fields;

namespace BOCCHI.Common.Config;

[Serializable]
[ConfigGroup("movement", GroupOrder = 10)]
public class MovementConfig : IAutoConfig
{
    [Checkbox(Order = 0, Section = "mount")]
    public bool SprintOnAetheryteApproach { get; set; } = true;

    [Checkbox(Order = 1, Section = "mount")]
    public bool ShouldAutoMount { get; set; } = true;

    [MountSelect(Order = 2, Section = "mount")]
    public uint PreferredMountId { get; set; } = 0;

    [Checkbox(Order = 3, Section = "unstuck")]
    public bool ShouldJumpWhenStuck { get; set; } = true;

    [JsonIgnore]
    public int JumpWhenStuckSeconds => 3;

    [Checkbox(Order = 5, Section = "travel")]
    public bool PreferReturnOverShardWalk { get; set; } = false;

    [RebuildPathMap(Order = 6, Section = "travel")]
    public bool RebuildPathMap { get; set; }
}
