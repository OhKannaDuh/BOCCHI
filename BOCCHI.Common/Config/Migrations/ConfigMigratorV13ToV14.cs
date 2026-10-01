using Newtonsoft.Json.Linq;

namespace BOCCHI.Common.Config.Migrations;

public class ConfigMigratorV13ToV14 : IMigrator
{
    public int FromVersion => 13;

    public int ToVersion => 14;

    public JObject Migrate(JObject oldConfig)
    {
        JObject result = (JObject)oldConfig.DeepClone();
        result["Version"] = ToVersion;

        JObject carrot = result["CarrotConfig"] as JObject
                         ?? new JObject { ["$type"] = "BOCCHI.Common.Config.CarrotConfig, BOCCHI.Common" };

        if (result["TreasureConfig"] is JObject treasure)
        {
            if (carrot["DrawLineToCarrots"] == null
                && treasure["DrawLineToCarrots"] is JToken drawLines)
            {
                carrot["DrawLineToCarrots"] = drawLines.DeepClone();
            }

            treasure.Remove("DrawLineToCarrots");
        }

        carrot.Remove("EnableCarrotHunt");
        result["CarrotConfig"] = carrot;
        return result;
    }
}
