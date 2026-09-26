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

    /// <summary>
    ///     While still walking to a FATE, only switch to a CE when registration has this many
    ///     seconds or fewer left. 0 = switch as soon as a CE is up (old behaviour). Once you are
    ///     in the FATE, it still finishes first (#187).
    /// </summary>
    [IntRange(0, 180, Order = 5, Section = "activities")]
    public int LeaveFateTravelForCeSeconds { get; set; } = 90;

    /// <summary>
    ///     Illegal Mode combat automation: Wrath/RSR + BOCCHI AI, or full BossMod / BMR autorotation.
    /// </summary>
    [EnumSelect<CombatAutorotation, CombatAutorotationDisplay, CombatAutorotationFilter>(Order = 6, Section = "combat")]
    public CombatAutorotation CombatAutorotation { get; set; } = CombatAutorotation.WrathCombo;

    /// <summary>
    ///     Wrath phantom-job option names (e.g. <c>Phantom_Berserker_Rage</c>) BOCCHI leaves off
    ///     when it takes over Wrath's Occult Crescent settings. Applied when Illegal Mode / Mob
    ///     Farmer starts.
    /// </summary>
    [WrathOccultOptionBlacklist(Order = 7, Indent = 1, Requires = nameof(UsesWrathCombo), Section = "combat")]
    [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
    public HashSet<string> DisabledWrathOccultOptions { get; set; } = [];

    public bool UsesWrathCombo => CombatAutorotation == CombatAutorotation.WrathCombo;

    /// <summary>
    ///     When on, rebuild BOCCHI's BossMod FATE/CE presets from the settings below when they
    ///     change, Illegal Mode starts, or you change job / melee / ranged. When off, existing
    ///     presets are kept until you press Update presets.
    /// </summary>
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

    /// <summary>
    ///     Stay mounted while a CE is preparing; dismount when it starts.
    /// </summary>
    [Checkbox(Order = 8, Section = "travel")]
    public bool StayMountedWhileWaitingForCe { get; set; } = false;

    /// <summary>
    ///     After FATE/CE: Return, teleport to the nearest aetheryte for the next activity, mount,
    ///     then stop — no auto-walk.
    /// </summary>
    [Checkbox(Order = 9, Section = "travel")]
    public bool StopAfterReturn { get; set; } = false;

    /// <summary>
    ///     When the current phantom job is maxed, switch to the next unlocked non-maxed job.
    /// </summary>
    [Checkbox(Order = 10, Section = "jobs")]
    public bool PhantomJobsLevelingMode { get; set; } = false;

    /// <summary>
    ///     While dead, accept a raise from another player. The return-to-camp death prompt is never
    ///     accepted — only the raise prompt that names the player casting it.
    ///     Shares Order 11 with Triage Mode; the name tie-break keeps it above Triage Mode and its
    ///     indented raise-job option.
    /// </summary>
    [Checkbox(Order = 11, Section = "triage")]
    public bool AutoAcceptRaise { get; set; } = true;

    /// <summary>
    ///     Seconds to wait after the raise prompt appears before accepting it. 0 = accept at once.
    ///     Accepts early anyway if the offer is about to expire. Order 11 + name keeps it directly
    ///     under Auto-accept raise.
    /// </summary>
    [IntRange(0, 30, Order = 11, Indent = 1, Requires = nameof(AutoAcceptRaise), Section = "triage")]
    public int AutoAcceptRaiseDelaySeconds { get; set; } = 0;

    /// <summary>
    ///     After FATE/CE: if raisable corpses are nearby, raise with the selected phantom job then continue.
    ///     No bodies → no swap / no wait; Illegal Mode continues as usual.
    /// </summary>
    [Checkbox(Order = 11, Section = "triage")]
    public bool EnableTriageMode { get; set; } = false;

    /// <summary>Which phantom job Triage Mode swaps to for raises (falls back if not unlocked).</summary>
    [TriageRaiseJob(Order = 12, Indent = 1, Requires = nameof(EnableTriageMode), Section = "triage")]
    public TriageRaiseJobPreference PreferredTriageRaiseJob { get; set; } = TriageRaiseJobPreference.PhantomChemist;

    /// <summary>
    ///     Illegal Mode / Completionist: after CE/FATE, Sight (if known) then hunt, or map hunt
    ///     without Sight. Only Illegal Mode reads this, so it belongs here rather than on the
    ///     Treasure page where people configuring Illegal Mode would not find it.
    /// </summary>
    [Checkbox(Order = 13, Section = "treasure")]
    public bool EnableAutomaticTreasureHuntDuringIllegalMode { get; set; } = false;

    /// <summary>
    ///     With Treasure Sight, pause Illegal Mode auto-hunt when a FATE is available (map hunt
    ///     without Sight always pauses for FATEs). Off keeps Sight hunts from yielding to FATEs.
    /// </summary>
    [Checkbox(
        Order = 14,
        Indent = 1,
        Requires = nameof(EnableAutomaticTreasureHuntDuringIllegalMode),
        Section = "treasure")]
    public bool PauseAutoTreasureHuntForFate { get; set; } = false;

    /// <summary>
    ///     Pause Illegal Mode auto-hunt when Pot timing leave-early says it is time to head to
    ///     the next pot (or a pot is already live). Uses the minutes on Pot timing.
    /// </summary>
    [Checkbox(
        Order = 15,
        Indent = 1,
        Requires = nameof(EnableAutomaticTreasureHuntDuringIllegalMode),
        Section = "treasure")]
    public bool PauseAutoTreasureHuntForPots { get; set; } = true;

    /// <summary>
    ///     With Treasure Sight, pause Illegal Mode auto-hunt when a CE is available (map hunt
    ///     without Sight always pauses for CEs). Off keeps Sight hunts from yielding to CEs.
    /// </summary>
    [Checkbox(
        Order = 16,
        Indent = 1,
        Requires = nameof(EnableAutomaticTreasureHuntDuringIllegalMode),
        Section = "treasure")]
    public bool PauseAutoTreasureHuntForCriticalEncounter { get; set; } = false;

    /// <summary>
    ///     Periodic camp Sight when auto-hunt is off. With auto-hunt on, idle camp Sight uses the
    ///     interval below instead (this toggle stays off / disabled).
    /// </summary>
    [Checkbox(
        Order = 17,
        Indent = 1,
        DisabledWhen = nameof(EnableAutomaticTreasureHuntDuringIllegalMode),
        Section = "treasure")]
    public bool ShouldCastTreasureSight { get; set; } = false;

    /// <summary>
    ///     Seconds between idle camp Treasure Sight casts (auto-hunt while waiting, or the camp
    ///     Sight toggle when auto-hunt is off).
    /// </summary>
    [IntRange(
        60,
        600,
        Order = 18,
        Indent = 2,
        Requires = nameof(UsesTreasureSightInterval),
        Section = "treasure")]
    public int TreasureSightRecastIntervalSeconds { get; set; } = 120;

    /// <summary>Interval slider: auto-hunt idle Sight, or camp Sight when auto-hunt is off.</summary>
    public bool UsesTreasureSightInterval =>
        EnableAutomaticTreasureHuntDuringIllegalMode || ShouldCastTreasureSight;

    /// <summary>Max random idle before Return; 0 delay when Treasure Sight is latched.</summary>
    [IntRange(2, 60, Order = 19, Section = "delays")]
    public int MaxRemoteIdleTimeSeconds { get; set; } = 10;

    /// <summary>
    ///     Upper bound (seconds) for a random 0..max idle at camp before teleporting to a FATE/CE.
    ///     0 = leave immediately.
    /// </summary>
    [IntRange(0, 60, Order = 20, Section = "delays")]
    public int MaxBaseTeleportDelaySeconds { get; set; } = 0;

    /// <summary>
    ///     Repair equipped gear when any piece falls to or below this condition (%).
    /// </summary>
    [IntRange(1, 99, Order = 21, Section = "repair")]
    public int AutoRepairThreshold { get; set; } = 30;

    /// <summary>Self-repair vs nearby mender at base camp.</summary>
    [EnumSelectDisplay<AutoRepairMethod, AutoRepairMethodDisplay>(Order = 22, Section = "repair")]
    public AutoRepairMethod AutoRepairMethod { get; set; } = AutoRepairMethod.SelfRepair;
}
