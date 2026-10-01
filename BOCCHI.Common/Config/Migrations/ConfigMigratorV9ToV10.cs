using Newtonsoft.Json.Linq;

namespace BOCCHI.Common.Config.Migrations;

public class ConfigMigratorV9ToV10 : IMigrator
{
    public int FromVersion => 9;

    public int ToVersion => 10;

    public JObject Migrate(JObject oldConfig)
    {
        JObject result = (JObject)oldConfig.DeepClone();
        result["Version"] = ToVersion;

        if (result["TreasureConfig"] is JObject treasure)
        {
            treasure.Remove("HuntCompleteSoundId");
            if (treasure["HuntCompleteSound"] == null)
            {
                treasure["HuntCompleteSound"] = "Moogle";
            }
        }

        return result;
    }
}
