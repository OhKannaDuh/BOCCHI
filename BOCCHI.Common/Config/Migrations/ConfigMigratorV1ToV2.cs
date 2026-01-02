using Newtonsoft.Json.Linq;

namespace BOCCHI.Common.Config.Migrations;

public class ConfigMigratorV1ToV2: IMigrator
{
    public int FromVersion { get; } = 1;

    public int ToVersion { get; } = 2;

    public JObject Migrate(JObject oldConfig)
    {
        return new JObject
        {
            ["$type"] = "BOCCHI.Config.Configuration, BOCCHI",
            ["Version"] = ToVersion,
            ["ExperienceConfig"] = new JObject
            {
                ["$type"] = "BOCCHI.Common.Config.ExperienceConfig, BOCCHI.Common",
                ["TrackedDuration"] = 5,
                ["GraphBucketSize"] = 15,
                ["ResetOnZoneChange"] = true,
            },
            ["CurrencyConfig"] = new JObject
            {
                ["$type"] = "BOCCHI.Common.Config.CurrencyConfig, BOCCHI.Common",
                ["TrackedDuration"] = 5,
                ["GraphBucketSize"] = 15,
                ["ResetOnZoneChange"] = true,
            },
            ["UIConfig"] = new JObject
            {
                ["$type"] = "BOCCHI.Common.Config.UIConfig, BOCCHI.Common",
                ["ShowExperienceTracker"] = oldConfig.BoolOr("ExpConfig.Enabled", true),
                ["ShowExperienceTrackerGraph"] = false,
                ["ShowCurrencyTracker"] = oldConfig.BoolOr("CurrencyConfig.Enabled", true),
                ["ShowCurrencyTrackerGraph"] = false,
            },
            ["BuffConfig"] = new JObject
            {
                ["$type"] = "BOCCHI.Common.Config.BuffConfig, BOCCHI.Common",
                ["ShouldAutomateBuffs"] = oldConfig.BoolOr("BuffConfig.Enabled", false),
                ["ApplyRomeosBallad"] = oldConfig.BoolOr("BuffConfig.ApplyRomeosBallad", false),
                ["ApplyEnduringFortitude"] = oldConfig.BoolOr("BuffConfig.ApplyEnduringFortitude", false),
                ["ApplyFleetfooted"] = oldConfig.BoolOr("BuffConfig.ApplyFleetfooted", false),
                ["ReapplyThreshold"] = oldConfig.IntOr("BuffConfig.ReapplyThreshold", 10),
                ["KnowledgeCrystalDistance"] = 30.0,
            },
        };
    }

}
