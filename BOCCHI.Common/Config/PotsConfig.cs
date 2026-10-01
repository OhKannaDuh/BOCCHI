using Ocelot.Config;
using Ocelot.Config.Fields;

namespace BOCCHI.Common.Config;

[Serializable]
[ConfigGroup("automation", GroupOrder = 0, Order = 1)]
public class PotsConfig : IAutoConfig
{
    [IntRange(0, 15, Order = 0, Section = "timing")]
    public int MinPotFateMinutesRemaining { get; set; }

    [IntRange(0, 15, Order = 1, Section = "timing")]
    public int PotSpawnLeadMinutes { get; set; } = 3;

    [IntRange(0, 30, Order = 2, Section = "timing")]
    public int FateFallbackCutoffMinutes { get; set; } = 5;

    [IntRange(0, 30, Order = 3, Section = "timing")]
    public int CeFallbackCutoffMinutes { get; set; } = 10;

    [Checkbox(Order = 4, Section = "chests")]
    public bool ShouldFarmRerollPotChests { get; set; } = true;

    public bool ShouldSkipLivePot(long timeRemainingSeconds) =>
        MinPotFateMinutesRemaining > 0
        && timeRemainingSeconds > 0
        && timeRemainingSeconds < MinPotFateMinutesRemaining * 60L;
}
