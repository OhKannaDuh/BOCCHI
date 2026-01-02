using Dalamud.Configuration;

namespace BOCCHI.Common.Config;

public interface IConfiguration : IPluginConfiguration
{
    ExperienceConfig ExperienceConfig { get; set; }

    CurrencyConfig CurrencyConfig { get; set; }

    UIConfig UIConfig { get; set; }

    BuffConfig BuffConfig { get; set; }
}
