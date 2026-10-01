using Newtonsoft.Json.Linq;

namespace BOCCHI.Common.Config.Migrations;

public class ConfigMigratorV16ToV17 : IMigrator
{
    public int FromVersion => 16;

    public int ToVersion => 17;

    public JObject Migrate(JObject oldConfig)
    {
        JObject result = (JObject)oldConfig.DeepClone();
        result["Version"] = ToVersion;

        if (result["BuffConfig"] is JObject buff)
        {
            buff.Remove("KnowledgeCrystalDistance");
        }

        return result;
    }
}
