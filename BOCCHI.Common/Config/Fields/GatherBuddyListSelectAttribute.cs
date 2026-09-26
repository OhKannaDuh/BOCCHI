using BOCCHI.Common.Config.Renderers;
using Ocelot.Config.Fields;

namespace BOCCHI.Common.Config.Fields;

/// <summary>Free-text GatherBuddy Reborn buy-list name with a picker fed by GBR IPC.</summary>
public sealed class GatherBuddyListSelectAttribute()
    : UIFieldAttribute(typeof(GatherBuddyListSelectRenderer))
{
    public int MaxLength { get; set; } = 128;
}
