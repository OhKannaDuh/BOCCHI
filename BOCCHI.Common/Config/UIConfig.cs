using Ocelot.Config;
using Ocelot.Config.Fields;

namespace BOCCHI.Common.Config;

[Serializable]
[ConfigGroup("ux")]
public class UIConfig : IAutoConfig
{
    [Checkbox] public bool ShowExperienceTracker { get; set; } = true;

    [Checkbox] public bool ShowExperienceTrackerGraph { get; set; } = false;

    [Checkbox] public bool ShowCurrencyTracker { get; set; } = true;

    [Checkbox] public bool ShowCurrencyTrackerGraph { get; set; } = false;
}
