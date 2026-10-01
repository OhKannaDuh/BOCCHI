using Newtonsoft.Json.Linq;

namespace BOCCHI.Common.Config.Migrations;

public class ConfigMigratorV11ToV12 : IMigrator
{
    public int FromVersion => 11;

    public int ToVersion => 12;

    public JObject Migrate(JObject oldConfig)
    {
        JObject result = (JObject)oldConfig.DeepClone();
        result["Version"] = ToVersion;
        return result;
    }
}
