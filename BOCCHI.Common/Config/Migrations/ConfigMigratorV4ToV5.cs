using Newtonsoft.Json.Linq;

namespace BOCCHI.Common.Config.Migrations;

public class ConfigMigratorV4ToV5 : IMigrator
{
    public int FromVersion => 4;

    public int ToVersion => 5;

    public JObject Migrate(JObject oldConfig)
    {
        JObject result = (JObject)oldConfig.DeepClone();
        result["Version"] = ToVersion;

        JObject? combat = result["CombatConfig"] as JObject;
        if (combat == null)
        {
            result.Remove("CombatConfig");
            return result;
        }

        JObject farmer = result["MobFarmerConfig"] as JObject
                         ?? new JObject { ["$type"] = "BOCCHI.Common.Config.MobFarmerConfig, BOCCHI.Common" };
        farmer["ShouldHandleTargeting"] = combat["ShouldHandleTargeting"] ?? true;
        farmer["ForceTargetCentralEnemy"] = combat["ForceTargetCentralEnemy"] ?? true;
        result["MobFarmerConfig"] = farmer;

        JObject automator = result["AutomatorConfig"] as JObject
                            ?? new JObject { ["$type"] = "BOCCHI.Common.Config.AutomatorConfig, BOCCHI.Common" };
        automator["AutoRepairThreshold"] = combat["AutoRepairThreshold"] ?? 30;
        result["AutomatorConfig"] = automator;

        result.Remove("CombatConfig");
        return result;
    }
}
