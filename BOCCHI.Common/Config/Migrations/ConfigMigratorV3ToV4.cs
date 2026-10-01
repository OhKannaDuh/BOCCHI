using Newtonsoft.Json.Linq;

namespace BOCCHI.Common.Config.Migrations;

public class ConfigMigratorV3ToV4 : IMigrator
{
    public int FromVersion => 3;

    public int ToVersion => 4;

    public JObject Migrate(JObject oldConfig)
    {
        JObject result = (JObject)oldConfig.DeepClone();
        result["Version"] = ToVersion;

        JObject? fates = result["FatesConfig"] as JObject;
        if (fates == null)
        {
            return result;
        }

        result["PotsConfig"] = new JObject
        {
            ["$type"] = "BOCCHI.Common.Config.PotsConfig, BOCCHI.Common",
            ["MinPotFateMinutesRemaining"] = fates["MinPotFateMinutesRemaining"] ?? 2,
            ["PotSpawnLeadMinutes"] = fates["PotSpawnLeadMinutes"] ?? 3,
            ["FateFallbackCutoffMinutes"] = fates["FateFallbackCutoffMinutes"] ?? 5,
            ["CeFallbackCutoffMinutes"] = fates["CeFallbackCutoffMinutes"] ?? 10,
            ["ShouldFarmRerollPotChests"] = fates["ShouldFarmRerollPotChests"] ?? true
        };

        fates.Remove("MinPotFateMinutesRemaining");
        fates.Remove("PotSpawnLeadMinutes");
        fates.Remove("FateFallbackCutoffMinutes");
        fates.Remove("CeFallbackCutoffMinutes");
        fates.Remove("ShouldFarmRerollPotChests");

        return result;
    }
}
