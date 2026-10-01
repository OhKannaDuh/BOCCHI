using Newtonsoft.Json.Linq;

namespace BOCCHI.Common.Config.Migrations;

public class ConfigMigratorV19ToV20 : IMigrator
{
    public int FromVersion => 19;

    public int ToVersion => 20;

    public JObject Migrate(JObject oldConfig)
    {
        JObject result = (JObject)oldConfig.DeepClone();
        result["Version"] = ToVersion;

        if (result["TreasureConfig"] is JObject treasure)
        {
            treasure.Remove("LastSouthHornStartHalf");
        }

        return result;
    }
}
