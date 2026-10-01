using Newtonsoft.Json.Linq;

namespace BOCCHI.Common.Config.Migrations;

public class ConfigMigratorV10ToV11 : IMigrator
{
    private static readonly string[] OrphanAutomatorKeys =
    [
        "StopAfterActivityAetheryte",
        "AutoSwitchNonMaxedPhantomJob",
    ];

    private static readonly string[] OrphanFatesKeys =
    [
        "ShouldDoFates",
        "ShouldDoCriticalEncounters",
        "PreferPotFates",
        "ShouldFarmPotChests",
        "ShouldPrepositionToPots",
        "MinPotFateMinutesRemaining",
        "PotSpawnLeadMinutes",
        "FateFallbackCutoffMinutes",
        "CeFallbackCutoffMinutes",
        "ShouldFarmRerollPotChests",
    ];

    public int FromVersion => 10;

    public int ToVersion => 11;

    public JObject Migrate(JObject oldConfig)
    {
        JObject result = (JObject)oldConfig.DeepClone();
        result["Version"] = ToVersion;

        JObject automator = result["AutomatorConfig"] as JObject
                            ?? new JObject { ["$type"] = "BOCCHI.Common.Config.AutomatorConfig, BOCCHI.Common" };

        if (automator["StopAfterReturn"] == null
            && automator["StopAfterActivityAetheryte"] is JToken oldStop)
        {
            automator["StopAfterReturn"] = oldStop.DeepClone();
        }

        foreach (string key in OrphanAutomatorKeys)
        {
            automator.Remove(key);
        }

        result["AutomatorConfig"] = automator;

        if (result["FatesConfig"] is JObject fates)
        {
            foreach (string key in OrphanFatesKeys)
            {
                fates.Remove(key);
            }
        }

        if (result["CriticalEncountersConfig"] is JObject ces)
        {
            ces.Remove("ShouldDoCriticalEncounters");
        }

        if (result["TreasureConfig"] is JObject treasure)
        {
            treasure.Remove("HuntCompleteSoundId");
            treasure.Remove("HuntReturnCost");
        }

        result.Remove("CombatConfig");
        result.Remove("ExperienceConfig");
        result.Remove("CurrencyConfig");

        return result;
    }
}
