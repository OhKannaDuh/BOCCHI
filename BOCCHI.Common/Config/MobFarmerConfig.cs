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

    [Checkbox(Order = 2, Section = "targets")]
    public bool ForceTargetCentralEnemy { get; set; } = true;

    [Checkbox(Order = 3, Section = "targets")]
    public bool ConsiderSpecialMobs { get; set; } = false;

    [IntRange(1, 50, Order = 4, Section = "targets")]
    public int MaxMobLevel { get; set; } = 40;

    [FloatRange(10f, 1000f, Order = 5, Section = "targets")]
    public float MaxEuclideanDistance { get; set; } = 75f;

    [FarmSpotList(Order = 6, Section = "spots")]
    public List<FarmSpot> Spots { get; set; } = [];

    [FloatRange(3f, 30f, Order = 7, Section = "spots")]
    public float ClaimedSpotSeconds { get; set; } = 8f;

    [FloatRange(3f, 15f, Order = 8, Section = "spots")]
    public float ClaimedPlayerRadius { get; set; } = 5f;

    [Checkbox(Order = 9, Section = "pulls")]
    public bool CountSpecialMobsTowardMinimum { get; set; } = false;

    [Checkbox(Order = 10, Section = "pulls")]
    public bool OnlyStartOutOfCombat { get; set; } = false;

    [IntRange(0, 20, Order = 11, Section = "pulls")]
    public int MinimumMobsToStartLoop { get; set; } = 0;

    [IntRange(1, 20, Order = 12, Section = "pulls")]
    public int MinimumMobsToStartFight { get; set; } = 5;

    [FloatRange(5f, 60f, Order = 13, Section = "pulls")]
    public float StackingTimeoutSeconds { get; set; } = 15f;

    [Checkbox(Order = 14, Section = "pulls")]
    public bool UseRangedPull { get; set; } = true;

    [Checkbox(Order = 15, Section = "pulls")]
    public bool UseProvoke { get; set; } = false;

    [Checkbox(Order = 16, Section = "pulls")]
    public bool UseGapCloser { get; set; } = false;

    [Checkbox(Order = 17, Section = "home")]
    public bool ReturnToStartInWaitingPhase { get; set; } = false;

    [FloatRange(10f, 1000f, Order = 18, Section = "home")]
    public float MinEuclideanDistanceToReturnHome { get; set; } = 200f;

    [Checkbox(Order = 19, Section = "buffs")]
    public bool ApplyBattleBell { get; set; } = false;

    [Checkbox(Order = 20, Section = "buffs")]
    public bool ApplyRingingRespite { get; set; } = false;

    [Checkbox(Order = 21, Section = "buffs")]
    public bool ApplyQuickstep { get; set; } = false;

    [Checkbox(Order = 22, Section = "buffs")]
    public bool ApplyCounterstance { get; set; } = false;

    [FloatRange(0f, 30f, Order = 23, Section = "buffs")]
    public float MaximumBattleBellWaitTime { get; set; } = 10f;

    [IntRange(0, 30, Order = 24, Section = "buffs")]
    public int QuickstepSkipIfRemainingMinutes { get; set; } = 0;

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

    [Checkbox(Order = 30, Section = "yields")]
    public bool YieldToCrystalBuffs { get; set; } = false;

    [Checkbox(Order = 31, Section = "debug")]
    public bool RenderDebugLines { get; set; } = false;
}
