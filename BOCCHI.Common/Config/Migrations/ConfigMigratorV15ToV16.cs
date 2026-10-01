using Newtonsoft.Json.Linq;

namespace BOCCHI.Common.Config.Migrations;

public class ConfigMigratorV15ToV16 : IMigrator
{
    public int FromVersion => 15;

    public int ToVersion => 16;

    public JObject Migrate(JObject oldConfig)
    {
        JObject result = (JObject)oldConfig.DeepClone();
        result["Version"] = ToVersion;

        if (result["TreasureConfig"] is JObject treasure)
        {
            treasure.Remove("HuntTeleportCost");
        }

        return result;
    }
}
