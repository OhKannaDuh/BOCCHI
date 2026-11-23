using Newtonsoft.Json.Linq;

namespace BOCCHI.Common.Config.Migrations;

public class ConfigMigratorV24ToV25 : IMigrator
{
    public int FromVersion => 24;

    public int ToVersion => 25;

    public JObject Migrate(JObject oldConfig)
    {
        JObject result = (JObject)oldConfig.DeepClone();
        result["Version"] = ToVersion;

        if (result["UIConfig"] is not JObject ui)
        {
            return result;
        }

        ui["ShowTrackerGraphs"] = Flag(ui, "ShowExperienceTrackerGraph", false) || Flag(ui, "ShowCurrencyTrackerGraph", false);
        ui["ShowRewardIcons"] = Flag(ui, "ShowDemiatmaDrops", true) || Flag(ui, "ShowNoteDrops", true) || Flag(ui, "ShowSoulShardDrops", true);
        foreach (string old in new[] { "ShowExperienceTrackerGraph", "ShowCurrencyTrackerGraph", "ShowDemiatmaDrops", "ShowNoteDrops", "ShowSoulShardDrops" })
        {
            ui.Remove(old);
        }

        return result;
    }

    private static bool Flag(JObject section, string name, bool fallback) =>
        section[name]?.Type == JTokenType.Boolean ? section[name]!.Value<bool>() : fallback;
}
