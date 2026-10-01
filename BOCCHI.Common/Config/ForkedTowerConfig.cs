using Ocelot.Config;
using Ocelot.Config.Fields;

namespace BOCCHI.Common.Config;

[Serializable]
[ConfigGroup("forked_tower", GroupOrder = 50)]
public class ForkedTowerConfig : IAutoConfig
{
    [Checkbox(Order = 0, Section = "helpers")]
    public bool DrawPotentialTrapPositions { get; set; } = true;

    [Checkbox(Order = 1, Section = "helpers")]
    public bool ShowRegistrationCountdown { get; set; } = true;

    [FloatRange(20f, 300f, Order = 2, Section = "helpers")]
    public float TrapDrawRange { get; set; } = 150f;

    [Checkbox(Order = 3, Section = "illegal_mode")]
    public bool AutoRegisterInIllegalMode { get; set; } = false;
}
