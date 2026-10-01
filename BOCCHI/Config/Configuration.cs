using BOCCHI.Common.Config;
using Ocelot.Config;

namespace BOCCHI.Config;

public class Configuration : IConfiguration
{
    public const int CurrentVersion = 24;

    [ConfigHidden] public int Version { get; set; } = CurrentVersion;

    [ConfigHidden] public string LastSeenPluginVersion { get; set; } = "";

    public UIConfig UIConfig { get; set; } = new();

    public DependenciesConfig DependenciesConfig { get; set; } = new();

    public LogsConfig LogsConfig { get; set; } = new();

    public AutomatorConfig AutomatorConfig { get; set; } = new();

    public MovementConfig MovementConfig { get; set; } = new();

    public BuffConfig BuffConfig { get; set; } = new();

    public MobFarmerConfig MobFarmerConfig { get; set; } = new();

    public FatesConfig FatesConfig { get; set; } = new();

    public PotsConfig PotsConfig { get; set; } = new();

    public CriticalEncountersConfig CriticalEncountersConfig { get; set; } = new();

    public TreasureConfig TreasureConfig { get; set; } = new();

    public ForkedTowerConfig ForkedTowerConfig { get; set; } = new();

    public ShoppingConfig ShoppingConfig { get; set; } = new();
}
