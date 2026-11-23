using BOCCHI.Common.Config;

namespace BOCCHI.Common.UI;

public static class QuickSettings
{
    public static readonly IReadOnlyDictionary<MainWindowSection, (Type Config, string Property)[]> Tabs =
        new Dictionary<MainWindowSection, (Type, string)[]>
        {
            [MainWindowSection.Automation] =
            [
                (typeof(AutomatorConfig), nameof(AutomatorConfig.CombatAutorotation)),
                (typeof(AutomatorConfig), nameof(AutomatorConfig.ShouldDoFates)),
                (typeof(AutomatorConfig), nameof(AutomatorConfig.ShouldDoCriticalEncounters)),
                (typeof(AutomatorConfig), nameof(AutomatorConfig.PreferPotFates)),
                (typeof(AutomatorConfig), nameof(AutomatorConfig.EnableAutomaticTreasureHuntDuringIllegalMode)),
                (typeof(BuffConfig), nameof(BuffConfig.ShouldAutomateBuffs)),
                (typeof(AutomatorConfig), nameof(AutomatorConfig.StopAfterReturn)),
                (typeof(AutomatorConfig), nameof(AutomatorConfig.AutoAcceptRaise)),
                (typeof(AutomatorConfig), nameof(AutomatorConfig.ReturnToBaseCampAfterDeath)),
            ],
            [MainWindowSection.PotsTreasure] =
            [
                (typeof(AutomatorConfig), nameof(AutomatorConfig.ShouldFarmPotChests)),
                (typeof(AutomatorConfig), nameof(AutomatorConfig.ShouldPrepositionToPots)),
                (typeof(PotsConfig), nameof(PotsConfig.ShouldFarmRerollPotChests)),
                (typeof(PotsConfig), nameof(PotsConfig.PotSpawnLeadMinutes)),
                (typeof(BuffConfig), nameof(BuffConfig.ShouldAutomateBuffs)),
            ],
            [MainWindowSection.Treasure] =
            [
                (typeof(TreasureConfig), nameof(TreasureConfig.HuntSilverChestsOnly)),
                (typeof(TreasureConfig), nameof(TreasureConfig.HuntMaxLevel)),
                (typeof(TreasureConfig), nameof(TreasureConfig.CastTreasureSightDuringHunt)),
                (typeof(TreasureConfig), nameof(TreasureConfig.ReturnToBaseCampAfterHunt)),
                (typeof(TreasureConfig), nameof(TreasureConfig.UseNinjaHideOnDangerousRoutes)),
                (typeof(TreasureConfig), nameof(TreasureConfig.LoopCarrotHunt)),
                (typeof(TreasureConfig), nameof(TreasureConfig.CarrotHuntScoutWithoutItem)),
            ],
            [MainWindowSection.MobFarmer] =
            [
                (typeof(MobFarmerConfig), nameof(MobFarmerConfig.MinimumMobsToStartLoop)),
                (typeof(MobFarmerConfig), nameof(MobFarmerConfig.MinimumMobsToStartFight)),
                (typeof(MobFarmerConfig), nameof(MobFarmerConfig.StackingTimeoutSeconds)),
                (typeof(MobFarmerConfig), nameof(MobFarmerConfig.MaxMobLevel)),
                (typeof(MobFarmerConfig), nameof(MobFarmerConfig.ConsiderSpecialMobs)),
                (typeof(MobFarmerConfig), nameof(MobFarmerConfig.YieldToPots)),
                (typeof(MobFarmerConfig), nameof(MobFarmerConfig.YieldToTreasureHunt)),
                (typeof(MobFarmerConfig), nameof(MobFarmerConfig.YieldToCrystalBuffs)),
            ],
        };
}
