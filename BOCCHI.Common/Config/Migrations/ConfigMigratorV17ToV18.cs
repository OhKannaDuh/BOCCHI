using Newtonsoft.Json.Linq;

namespace BOCCHI.Common.Config.Migrations;

public class ConfigMigratorV17ToV18 : IMigrator
{
    public int FromVersion => 17;

    public int ToVersion => 18;

    public JObject Migrate(JObject oldConfig)
    {
        JObject result = (JObject)oldConfig.DeepClone();
        result["Version"] = ToVersion;

        JObject ui = JObjectExtensions.EnsureObject(
            result, "UIConfig", "BOCCHI.Common.Config.UIConfig, BOCCHI.Common");

        if (result["EventDropConfig"] is JObject drops)
        {
            JObjectExtensions.MoveIfPresent(
                drops, ui, "ShowDemiatmaDrops", "ShowNoteDrops", "ShowSoulShardDrops");
        }

        result.Remove("EventDropConfig");
        return result;
    }
}
