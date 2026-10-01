using Newtonsoft.Json.Linq;

namespace BOCCHI.Common.Config.Migrations;

public class ConfigMigratorV14ToV15 : IMigrator
{
    public int FromVersion => 14;

    public int ToVersion => 15;

    public JObject Migrate(JObject oldConfig)
    {
        JObject result = (JObject)oldConfig.DeepClone();
        result["Version"] = ToVersion;

        JObject treasure = result["TreasureConfig"] as JObject
                           ?? new JObject { ["$type"] = "BOCCHI.Common.Config.TreasureConfig, BOCCHI.Common" };

        if (result["CarrotConfig"] is JObject carrot)
        {
            if (treasure["DrawLineToCarrots"] == null
                && carrot["DrawLineToCarrots"] is JToken drawLines)
            {
                treasure["DrawLineToCarrots"] = drawLines.DeepClone();
            }
        }

        treasure.Remove("HuntDetectionRange");
        treasure.Remove("HuntTeleportCost");
        treasure.Remove("EnableCarrotHunt");
        treasure.Remove("CarrotHuntDetectionRange");
        result["TreasureConfig"] = treasure;
        result.Remove("CarrotConfig");
        return result;
    }
}
