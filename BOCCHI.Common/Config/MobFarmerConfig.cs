using Newtonsoft.Json;
using BOCCHI.Common.Config.Fields;
using BOCCHI.Common.Data.Mobs;
using BOCCHI.Common.Data.MobFarmer;
using Ocelot.Config;
using Ocelot.Config.Fields;

namespace BOCCHI.Common.Config;

[Serializable]
[ConfigGroup("mob_farmer", GroupOrder = 19)]
public class MobFarmerConfig : IAutoConfig
{
    [MobMultiSelect(Order = 0, Section = "targets")]
    public List<Mob> Mobs { get; set; } = [];

    [Checkbox(Order = 1, Section = "targets")]
    public bool ShouldHandleTargeting { get; set; } = true;

    [JsonIgnore]
    public bool ForceTargetCentralEnemy => true;

    [Checkbox(Order = 3, Section = "targets")]
    public bool ConsiderSpecialMobs { get; set; } = false;

    [IntRange(1, 50, Order = 4, Section = "targets")]
    public int MaxMobLevel { get; set; } = 40;

    [FloatRange(10f, 100f, Format = "%.0f", Order = 5, Section = "targets")]
    public float MaxEuclideanDistance { get; set; } = 50f;

    [FarmSpotList(Order = 6, Section = "spots")]
    public List<FarmSpot> Spots { get; set; } = [];

    [JsonIgnore]
    public float ClaimedSpotSeconds => 8f;

    [JsonIgnore]
    public float ClaimedPlayerRadius => 5f;

    [JsonIgnore]
    public bool CountSpecialMobsTowardMinimum => false;

    [IntRange(0, 20, Order = 11, Section = "pulls")]
    public int MinimumMobsToStartLoop { get; set; } = 0;

    [IntRange(1, 20, Order = 12, Section = "pulls")]
    public int MinimumMobsToStartFight { get; set; } = 5;

    [FloatRange(5f, 60f, Format = "%.0f", Order = 13, Section = "pulls")]
    public float StackingTimeoutSeconds { get; set; } = 15f;

    [IntRange(0, 100, Order = 10, Section = "pulls")]
    public int MinimumHpPercentToPull { get; set; } = 80;

    [Checkbox(Order = 14, Section = "pulls")]
    public bool UseRangedPull { get; set; } = true;

    [Checkbox(Order = 15, Section = "pulls")]
    public bool UseProvoke { get; set; } = false;

    [Checkbox(Order = 16, Section = "pulls")]
    public bool UseGapCloser { get; set; } = false;

    [JsonIgnore]
    public bool ReturnToStartInWaitingPhase => false;

    [JsonIgnore]
    public float MinEuclideanDistanceToReturnHome => 200f;

    [Checkbox(Order = 19, Section = "buffs")]
    public bool ApplyBattleBell { get; set; } = false;

    [Checkbox(Order = 20, Section = "buffs")]
    public bool ApplyRingingRespite { get; set; } = false;

    [Checkbox(Order = 21, Section = "buffs")]
    public bool ApplyQuickstep { get; set; } = false;

    [Checkbox(Order = 22, Section = "buffs")]
    public bool ApplyCounterstance { get; set; } = false;

    [JsonIgnore]
    public float MaximumBattleBellWaitTime => 10f;

    [JsonIgnore]
    public int QuickstepSkipIfRemainingMinutes => 0;

    [Checkbox(Order = 25, Section = "yields")]
    public bool YieldToPots { get; set; } = false;

    [Checkbox(Order = 26, Section = "yields")]
    public bool CastTreasureSightAtFarm { get; set; } = false;

    [IntRange(5, 60, Order = 27, Indent = 1, Requires = nameof(CastTreasureSightAtFarm), Section = "yields")]
    public int TreasureSightIntervalMinutes { get; set; } = 15;

    [Checkbox(Order = 28, Section = "yields")]
    public bool YieldToTreasureHunt { get; set; } = false;

    [IntRange(5, 60, Order = 29, Indent = 1, Requires = nameof(YieldToTreasureHunt), Section = "yields")]
    public int TreasureHuntIntervalMinutes { get; set; } = 15;

    [Checkbox(Order = 30, Indent = 1, Requires = nameof(YieldToTreasureHunt), Section = "yields")]
    public bool TreasureHuntPreferPots { get; set; } = false;

    [Checkbox(Order = 31, Section = "yields")]
    public bool YieldToCrystalBuffs { get; set; } = false;

    [JsonIgnore]
    public bool RenderDebugLines => false;
}
