using BOCCHI.Common.Config.Fields;
using Ocelot.Config;
using Ocelot.Config.Fields;

namespace BOCCHI.Common.Config;

[Serializable]
[ConfigGroup("treasure", GroupOrder = 20)]
public class TreasureConfig : IAutoConfig
{
    [Checkbox(Order = 0, Section = "shared_maps")]
    public bool EnableSharedMaps { get; set; } = true;

    [Checkbox(Order = 1, Section = "radar")]
    public bool DrawLineToBronzeChests { get; set; } = true;

    [Checkbox(Order = 2, Section = "radar")]
    public bool DrawLineToSilverChests { get; set; } = true;

    [Checkbox(Order = 3, Section = "radar")]
    public bool DrawLineToCarrots { get; set; } = true;

    [Checkbox(Order = 4, Section = "radar")]
    public bool ShowPercentageActiveTreasureCount { get; set; } = false;

    [Checkbox(Order = 5, Section = "completion")]
    public bool ReturnToBaseCampAfterHunt { get; set; } = true;

    [Checkbox(Order = 6, Section = "completion")]
    public bool PlaySoundOnHuntComplete { get; set; } = true;

    [Mp3SoundSelect(Order = 7, Section = "completion")]
    public string HuntCompleteSound { get; set; } = "Moogle";

    [Checkbox(Order = 8, Section = "carrot_hunt")]
    public bool LoopCarrotHunt { get; set; } = false;

    [Checkbox(Order = 9, Section = "treasure_hunt")]
    public bool CastTreasureSightDuringHunt { get; set; } = true;

    [IntRange(1, 50, Order = 10, Indent = 1, Requires = nameof(CastTreasureSightDuringHunt), Section = "treasure_hunt")]
    public int TreasureSightEveryNLocations { get; set; } = 10;

    [IntRange(1, 50, Order = 11, Section = "treasure_hunt")]
    public int HuntMaxLevel { get; set; } = 50;

    [Checkbox(Order = 12, Section = "treasure_hunt")]
    public bool HuntSilverChestsOnly { get; set; } = false;

    [IntRange(0, 100, Order = 13, Section = "treasure_hunt")]
    public int HuntMinBronzePercent { get; set; } = 50;

    [IntRange(0, 100, Order = 14, Section = "treasure_hunt")]
    public int HuntMinSilverPercent { get; set; } = 50;

    [FloatRange(10f, 60f, Order = 15, Section = "treasure_hunt")]
    public float EmptyPadTrustDistance { get; set; } = 60f;

    [Checkbox(Order = 16, Section = "treasure_hunt")]
    public bool SkipUnsafeTreasureWindows { get; set; } = true;

    [Checkbox(Order = 17, Section = "ninja_hide")]
    public bool UseNinjaHideOnDangerousRoutes { get; set; } = false;

    [Checkbox(Order = 18, Section = "ninja_hide")]
    public bool UseOccultSprintWhileHidden { get; set; } = false;

    [IntRange(0, 100, Order = 19, Section = "ninja_hide")]
    public int NinjaGearsetNumber { get; set; } = 0;

    [IntRange(-5, 10, Order = 20, Section = "ninja_hide")]
    public int KnowledgeHideOffset { get; set; } = 0;

    [FloatRange(5f, 40f, Order = 21, Section = "ninja_hide")]
    public float KnowledgeThreatEnterDistance { get; set; } = 10f;

    [FloatRange(10f, 60f, Order = 22, Section = "ninja_hide")]
    public float KnowledgeThreatExitDistance { get; set; } = 20f;

    [ConfigHidden]
    public string? LastSouthHornStartSegment { get; set; }
}
