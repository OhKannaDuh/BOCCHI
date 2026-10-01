using Newtonsoft.Json.Linq;

namespace BOCCHI.Common.Config.Migrations;

public class ConfigMigratorV20ToV21 : IMigrator
{
    public int FromVersion => 20;

    public int ToVersion => 21;

    public JObject Migrate(JObject oldConfig)
    {
        JObject result = (JObject)oldConfig.DeepClone();
        result["Version"] = ToVersion;

        JObject automator = JObjectExtensions.EnsureObject(
            result, "AutomatorConfig", "BOCCHI.Common.Config.AutomatorConfig, BOCCHI.Common");

        bool aiOn = automator.BoolOr("ToggleAiProvider", true);
        automator["CombatAutorotation"] = aiOn ? 1 : 0;
        automator.Remove("ToggleAiProvider");
        automator.Remove("JobAutorotation");

        return result;
    }
}
