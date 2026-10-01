using Newtonsoft.Json.Linq;

namespace BOCCHI.Common.Config.Migrations;

public class ConfigMigratorV8ToV9 : IMigrator
{
    public int FromVersion => 8;

    public int ToVersion => 9;

    public JObject Migrate(JObject oldConfig)
    {
        JObject result = (JObject)oldConfig.DeepClone();
        result["Version"] = ToVersion;

        JObject automator = result["AutomatorConfig"] as JObject
                            ?? new JObject { ["$type"] = "BOCCHI.Common.Config.AutomatorConfig, BOCCHI.Common" };

        if (result["FatesConfig"] is JObject fates)
        {
            automator["ShouldPrepositionToPots"] = fates["ShouldPrepositionToPots"] ?? true;
            fates.Remove("ShouldPrepositionToPots");
        }

        result["AutomatorConfig"] = automator;
        return result;
    }
}
