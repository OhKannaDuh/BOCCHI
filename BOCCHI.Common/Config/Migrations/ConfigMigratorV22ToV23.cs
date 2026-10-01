using Newtonsoft.Json.Linq;

namespace BOCCHI.Common.Config.Migrations;

public class ConfigMigratorV22ToV23 : IMigrator
{
    public int FromVersion => 22;

    public int ToVersion => 23;

    public JObject Migrate(JObject oldConfig)
    {
        JObject result = (JObject)oldConfig.DeepClone();
        result["Version"] = ToVersion;

        JObject movement = JObjectExtensions.EnsureObject(
            result, "MovementConfig", "BOCCHI.Common.Config.MovementConfig, BOCCHI.Common");

        if (result["AutomatorConfig"] is not JObject automator)
        {
            return result;
        }

        string[] moved =
        [
            "SprintOnAetheryteApproach",
            "ShouldAutoMount",
            "PreferredMountId",
            "ShouldJumpWhenStuck",
            "JumpWhenStuckSeconds",
        ];

        JObjectExtensions.MoveIfPresent(automator, movement, moved);
        foreach (string key in moved)
        {
            automator.Remove(key);
        }

        if (result["TreasureConfig"] is JObject treasure)
        {
            const string autoHunt = "EnableAutomaticTreasureHuntDuringIllegalMode";
            JObjectExtensions.MoveIfPresent(treasure, automator, autoHunt);
            treasure.Remove(autoHunt);
        }

        return result;
    }
}
