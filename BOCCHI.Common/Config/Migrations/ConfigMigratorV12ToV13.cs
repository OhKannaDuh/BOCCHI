using Newtonsoft.Json.Linq;

namespace BOCCHI.Common.Config.Migrations;

public class ConfigMigratorV12ToV13 : IMigrator
{
    public int FromVersion => 12;

    public int ToVersion => 13;

    public JObject Migrate(JObject oldConfig)
    {
        JObject result = (JObject)oldConfig.DeepClone();
        result["Version"] = ToVersion;

        JObject carrot = result["CarrotConfig"] as JObject
                         ?? new JObject { ["$type"] = "BOCCHI.Common.Config.CarrotConfig, BOCCHI.Common" };

        if (result["TreasureConfig"] is JObject treasure)
        {
            if (carrot["CarrotHuntDetectionRange"] == null
                && treasure["CarrotHuntDetectionRange"] is JToken range)
            {
                carrot["CarrotHuntDetectionRange"] = range.DeepClone();
            }

            treasure.Remove("CarrotHuntDetectionRange");
            treasure.Remove("EnableCarrotHunt");
        }

        carrot.Remove("EnableCarrotHunt");
        result["CarrotConfig"] = carrot;
        return result;
    }
}
