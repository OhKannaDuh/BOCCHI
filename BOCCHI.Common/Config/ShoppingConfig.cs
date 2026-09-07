using Ocelot.Config;
using Ocelot.Config.Fields;

namespace BOCCHI.Common.Config;

[Serializable]
[ConfigGroup("shopping", GroupOrder = 25)]
public class ShoppingConfig : IAutoConfig
{
    [Checkbox(Order = 0, Section = "auto")]
    public bool EnableAutoShop { get; set; } = false;

    /// <summary>0 = never start from silver.</summary>
    [IntRange(0, 9999, Order = 1, Section = "auto", Requires = nameof(EnableAutoShop))]
    public int SilverThreshold { get; set; } = 8000;

    /// <summary>0 = never start from gold.</summary>
    [IntRange(0, 9999, Order = 2, Section = "auto", Requires = nameof(EnableAutoShop))]
    public int GoldThreshold { get; set; } = 0;
}
