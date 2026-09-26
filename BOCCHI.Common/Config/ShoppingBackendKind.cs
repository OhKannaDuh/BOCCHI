using Ocelot.Config.Renderers.Enum;

namespace BOCCHI.Common.Config;

/// <summary>Which plugin performs Occult Crescent auto-shopping purchases.</summary>
public enum ShoppingBackendKind
{
    /// <summary>GatherBuddy Reborn vendor buy list (named in <see cref="ShoppingConfig.GatherBuddyListName"/>).</summary>
    GatherBuddyReborn = 0,

    /// <summary>Knightshopper's Occult Crescent list.</summary>
    Knightshopper = 1,
}

public sealed class ShoppingBackendKindDisplay : IEnumDisplay<ShoppingBackendKind>
{
    public string Display(ShoppingBackendKind value) => value switch
    {
        ShoppingBackendKind.Knightshopper => "Knightshopper",
        _ => "GatherBuddy Reborn",
    };
}
