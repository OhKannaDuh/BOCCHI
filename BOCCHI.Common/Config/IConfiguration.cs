using Dalamud.Configuration;
namespace BOCCHI.Common.Config;

public interface IConfiguration : IPluginConfiguration
{
    UIConfig UIConfig { get; set; }

    DependenciesConfig DependenciesConfig { get; set; }

    LogsConfig LogsConfig { get; set; }

    AutomatorConfig AutomatorConfig { get; set; }

    MovementConfig MovementConfig { get; set; }

    BuffConfig BuffConfig { get; set; }

    RaiseConfig RaiseConfig { get; set; }

    MobFarmerConfig MobFarmerConfig { get; set; }

    FatesConfig FatesConfig { get; set; }

    PotsConfig PotsConfig { get; set; }

    CriticalEncountersConfig CriticalEncountersConfig { get; set; }

    TreasureConfig TreasureConfig { get; set; }

    ForkedTowerConfig ForkedTowerConfig { get; set; }

    ShoppingConfig ShoppingConfig { get; set; }
}
