namespace BOCCHI.Services.Shopping;

/// <summary>Occult Crescent auto-shopping — stoppable from Emergency Stop / mode teardown.</summary>
public interface IShoppingService
{
    /// <summary>True while shopping owns pathing or has soft-suspended other modes.</summary>
    bool IsActive { get; }

    /// <summary>Abort travel/buy immediately and release soft-suspends.</summary>
    void ForceStop();
}
