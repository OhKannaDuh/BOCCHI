using Newtonsoft.Json.Linq;

namespace BOCCHI.Common.Config.Migrations;

public class ConfigMigratorV7ToV8 : IMigrator
{
    public int FromVersion => 7;

    public int ToVersion => 8;

    public JObject Migrate(JObject oldConfig)
    {
        JObject result = (JObject)oldConfig.DeepClone();
        result["Version"] = ToVersion;

        JObject automator = result["AutomatorConfig"] as JObject
                            ?? new JObject { ["$type"] = "BOCCHI.Common.Config.AutomatorConfig, BOCCHI.Common" };

        if (result["FatesConfig"] is JObject fates)
        {
            automator["ShouldFarmPotChests"] = fates["ShouldFarmPotChests"] ?? false;
            fates.Remove("ShouldFarmPotChests");
        }

        result["AutomatorConfig"] = automator;
        return result;
    }
}
