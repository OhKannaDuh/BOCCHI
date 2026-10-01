namespace BOCCHI.MobFarmer.Services;

public sealed class MobFarmerPanelState
{
    private static readonly TimeSpan VisibleGrace = TimeSpan.FromSeconds(2);

    private DateTime lastRenderedUtc = DateTime.MinValue;

    public void MarkRendered() => lastRenderedUtc = DateTime.UtcNow;

    public bool RecentlyVisible => DateTime.UtcNow - lastRenderedUtc <= VisibleGrace;
}
