using BOCCHI.Common.Config.Fields;
using Newtonsoft.Json;
using Ocelot.Config;
using Ocelot.Config.Fields;

namespace BOCCHI.Common.Config;

[Serializable]
[ConfigGroup("automation", GroupOrder = 0, Order = 0)]
public class AutomatorConfig : IAutoConfig
{
    [Checkbox(Order = 0, Section = "activities")]
    public bool ShouldDoFates { get; set; } = true;

    [Checkbox(Order = 1, Section = "activities")]
    public bool PreferPotFates { get; set; } = false;

    [Checkbox(Order = 2, Section = "activities")]
    public bool ShouldFarmPotChests { get; set; } = false;

    [Checkbox(Order = 3, Section = "activities")]
    public bool ShouldPrepositionToPots { get; set; } = true;

    [Checkbox(Order = 4, Section = "activities")]
    public bool ShouldDoCriticalEncounters { get; set; } = true;

    [IntRange(0, 180, Order = 5, Section = "activities")]
    public int LeaveFateTravelForCeSeconds { get; set; } = 90;

    [EnumSelect<CombatAutorotation, CombatAutorotationDisplay, CombatAutorotationFilter>(Order = 6, Section = "combat")]
    public CombatAutorotation CombatAutorotation { get; set; } = CombatAutorotation.WrathCombo;

    [WrathOccultOptionBlacklist(Order = 7, Indent = 1, Requires = nameof(UsesWrathCombo), Section = "combat")]
    [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
    public HashSet<string> DisabledWrathOccultOptions { get; set; } = [];

    public bool UsesWrathCombo => CombatAutorotation == CombatAutorotation.WrathCombo;

    [BossModPresetOptions(Order = 8, Indent = 1, Section = "combat")]
    public bool UpdateBossModPresetsAutomatically { get; set; } = false;

    public bool BossModMaxDistanceByRole { get; set; } = true;

    public bool BossModMeleeOnHitbox { get; set; } = true;

    public float BossModMaxDistance { get; set; } = 15f;

    public float BossModMaxDistanceMelee { get; set; } = 2.6f;

    public float BossModMaxDistanceRanged { get; set; } = 15f;

    public BossModOverdodge BossModOverdodge { get; set; } = BossModOverdodge.None;

    public BossModMovementDelay BossModMovementDelay { get; set; } = BossModMovementDelay.None;

    public bool BossModSeparateDodgeDelay { get; set; } = false;

    public BossModMovementDelay BossModDodgeMovementDelay { get; set; } = BossModMovementDelay.None;

    [Checkbox(Order = 8, Section = "travel")]
    public bool StayMountedWhileWaitingForCe { get; set; } = false;

    [Checkbox(Order = 9, Section = "travel")]
    public bool StopAfterReturn { get; set; } = false;

    [Checkbox(Order = 10, Section = "jobs")]
    public bool PhantomJobsLevelingMode { get; set; } = false;

    [Checkbox(Order = 11, Section = "triage")]
    public bool EnableTriageMode { get; set; } = false;

    [TriageRaiseJob(Order = 12, Indent = 1, Requires = nameof(EnableTriageMode), Section = "triage")]
    public TriageRaiseJobPreference PreferredTriageRaiseJob { get; set; } = TriageRaiseJobPreference.PhantomChemist;

    [Checkbox(Order = 13, Section = "treasure")]
    public bool EnableAutomaticTreasureHuntDuringIllegalMode { get; set; } = false;

    [Checkbox(
        Order = 14,
        Indent = 1,
        Requires = nameof(EnableAutomaticTreasureHuntDuringIllegalMode),
        Section = "treasure")]
    public bool PauseAutoTreasureHuntForFate { get; set; } = false;

    [Checkbox(
        Order = 15,
        Indent = 1,
        Requires = nameof(EnableAutomaticTreasureHuntDuringIllegalMode),
        Section = "treasure")]
    public bool PauseAutoTreasureHuntForPots { get; set; } = true;

    [Checkbox(
        Order = 16,
        Indent = 1,
        Requires = nameof(EnableAutomaticTreasureHuntDuringIllegalMode),
        Section = "treasure")]
    public bool PauseAutoTreasureHuntForCriticalEncounter { get; set; } = false;

    [Checkbox(
        Order = 17,
        Indent = 1,
        DisabledWhen = nameof(EnableAutomaticTreasureHuntDuringIllegalMode),
        Section = "treasure")]
    public bool ShouldCastTreasureSight { get; set; } = false;

    [IntRange(
        60,
        600,
        Order = 18,
        Indent = 2,
        Requires = nameof(UsesTreasureSightInterval),
        Section = "treasure")]
    public int TreasureSightRecastIntervalSeconds { get; set; } = 120;

    public bool UsesTreasureSightInterval =>
        EnableAutomaticTreasureHuntDuringIllegalMode || ShouldCastTreasureSight;

    [IntRange(2, 60, Order = 19, Section = "delays")]
    public int MaxRemoteIdleTimeSeconds { get; set; } = 10;

    [IntRange(0, 60, Order = 20, Section = "delays")]
    public int MaxBaseTeleportDelaySeconds { get; set; } = 0;

    [IntRange(1, 99, Order = 21, Section = "repair")]
    public int AutoRepairThreshold { get; set; } = 30;

    [EnumSelectDisplay<AutoRepairMethod, AutoRepairMethodDisplay>(Order = 22, Section = "repair")]
    public AutoRepairMethod AutoRepairMethod { get; set; } = AutoRepairMethod.SelfRepair;
}
