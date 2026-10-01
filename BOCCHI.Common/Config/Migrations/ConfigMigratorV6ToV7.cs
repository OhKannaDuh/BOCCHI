using Newtonsoft.Json.Linq;

namespace BOCCHI.Common.Config.Migrations;

public class ConfigMigratorV6ToV7 : IMigrator
{
    public int FromVersion => 6;

    public int ToVersion => 7;

    public JObject Migrate(JObject oldConfig)
    {
        JObject result = (JObject)oldConfig.DeepClone();
        result["Version"] = ToVersion;

        JObject automator = result["AutomatorConfig"] as JObject
                            ?? new JObject { ["$type"] = "BOCCHI.Common.Config.AutomatorConfig, BOCCHI.Common" };

        if (result["FatesConfig"] is JObject fates)
        {
            automator["PreferPotFates"] = fates["PreferPotFates"] ?? false;
            fates.Remove("PreferPotFates");
        }

        result["AutomatorConfig"] = automator;
        return result;
    }
}
