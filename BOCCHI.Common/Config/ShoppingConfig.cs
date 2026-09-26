using BOCCHI.Common.Config.Fields;
using Newtonsoft.Json;
using Ocelot.Config;
using Ocelot.Config.Fields;

namespace BOCCHI.Common.Config;

[Serializable]
[ConfigGroup("shopping", GroupOrder = 25)]
public class ShoppingConfig : IAutoConfig
{
    public const string DefaultGatherBuddyListName = "新月岛";

    [Checkbox(Order = 0, Section = "auto")]
    public bool EnableAutoShop { get; set; } = false;

    /// <summary>0 = never start from silver.</summary>
    [IntRange(0, 9999, Order = 1, Section = "auto", Requires = nameof(EnableAutoShop))]
    public int SilverThreshold { get; set; } = 8000;

    /// <summary>0 = never start from gold.</summary>
    [IntRange(0, 9999, Order = 2, Section = "auto", Requires = nameof(EnableAutoShop))]
    public int GoldThreshold { get; set; } = 0;

    [EnumSelectDisplay<ShoppingBackendKind, ShoppingBackendKindDisplay>(Order = 3, Section = "backend")]
    public ShoppingBackendKind Backend { get; set; } = ShoppingBackendKind.GatherBuddyReborn;

    /// <summary>GBR Vendors-tab buy list to run (case-insensitive). Empty = GBR's active list.</summary>
    [GatherBuddyListSelect(Order = 4, Section = "backend", DisabledWhen = nameof(UsesKnightshopper))]
    public string GatherBuddyListName { get; set; } = DefaultGatherBuddyListName;

    [JsonIgnore]
    public bool UsesKnightshopper => Backend == ShoppingBackendKind.Knightshopper;
}
