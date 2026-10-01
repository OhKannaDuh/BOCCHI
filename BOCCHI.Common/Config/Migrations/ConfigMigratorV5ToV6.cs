using Newtonsoft.Json.Linq;

namespace BOCCHI.Common.Config.Migrations;

public class ConfigMigratorV5ToV6 : IMigrator
{
    public int FromVersion => 5;

    public int ToVersion => 6;

    public JObject Migrate(JObject oldConfig)
    {
        JObject result = (JObject)oldConfig.DeepClone();
        result["Version"] = ToVersion;

        JObject automator = result["AutomatorConfig"] as JObject
                            ?? new JObject { ["$type"] = "BOCCHI.Common.Config.AutomatorConfig, BOCCHI.Common" };

        if (result["FatesConfig"] is JObject fates)
        {
            automator["ShouldDoFates"] = fates["ShouldDoFates"] ?? true;
            fates.Remove("ShouldDoFates");
        }

        if (result["CriticalEncountersConfig"] is JObject ces)
        {
            automator["ShouldDoCriticalEncounters"] = ces["ShouldDoCriticalEncounters"] ?? true;
            ces.Remove("ShouldDoCriticalEncounters");
        }

        result["AutomatorConfig"] = automator;
        return result;
    }
}
