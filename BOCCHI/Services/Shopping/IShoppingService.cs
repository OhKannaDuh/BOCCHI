namespace BOCCHI.Services.Shopping;

/// <summary>Occult Crescent auto-shopping — stoppable from Emergency Stop / mode teardown.</summary>
public interface IShoppingService
{
    /// <summary>True while shopping owns pathing or has soft-suspended other modes.</summary>
    bool IsActive { get; }

    /// <summary>Abort travel/buy immediately and release soft-suspends.</summary>
    void ForceStop();

    /// <summary>
    ///     Debug / manual: Return to base camp if needed, then start Knightshopper’s Occult Crescent
    ///     list. Works even when auto-shop is off. Returns false with a reason in <paramref name="detail"/>.
    /// </summary>
    bool TryForceStart(out string detail);

    /// <summary>One-line status for chat (phase, camp, Knightshopper op).</summary>
    string DescribeStatus();
}
