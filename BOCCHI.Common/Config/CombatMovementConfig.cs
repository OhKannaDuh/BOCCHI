using BOCCHI.Common.Config.Fields;
using Newtonsoft.Json;
using Ocelot.Config;

namespace BOCCHI.Common.Config;

// The values live in AutomatorConfig (presets read them there); this page only draws them.
[Serializable]
[ConfigGroup("automation", GroupOrder = 0, Order = 5)]
public class CombatMovementConfig : IAutoConfig
{
    [BossModPresetOptions(Order = 0)]
    [JsonIgnore]
    public bool UpdateBossModPresetsAutomatically { get; set; }
}
