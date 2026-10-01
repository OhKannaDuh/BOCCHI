using Newtonsoft.Json.Linq;

namespace BOCCHI.Common.Config.Migrations;

public class ConfigMigratorV18ToV19 : IMigrator
{
    public int FromVersion => 18;

    public int ToVersion => 19;

    public JObject Migrate(JObject oldConfig)
    {
        JObject result = (JObject)oldConfig.DeepClone();
        result["Version"] = ToVersion;

        JObject ui = JObjectExtensions.EnsureObject(
            result, "UIConfig", "BOCCHI.Common.Config.UIConfig, BOCCHI.Common");

        if (result["TrackerConfig"] is JObject tracker)
        {
            JObjectExtensions.MoveIfPresent(tracker, ui, "TrackedDuration", "GraphBucketSize");
        }

        result.Remove("TrackerConfig");
        return result;
    }
}
