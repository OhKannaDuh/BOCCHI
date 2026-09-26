namespace BOCCHI.Common.Ipc.GatherBuddy;

/// <summary>GatherBuddy Reborn vendor buy-list IPC (<c>GatherBuddyReborn.VendorBuyList*</c>).</summary>
public interface IGatherBuddyIpc
{
    /// <summary>GBR is loaded and exposes the vendor buy-list gates (IPC version ≥ 3).</summary>
    bool IsAvailable { get; }

    /// <summary>GBR IPC version, or 0 when unreachable.</summary>
    int Version { get; }

    /// <summary>A buy list is running or GBR is still leaving a vendor interaction.</summary>
    bool IsBusy { get; }

    IReadOnlyList<string> ListNames();

    /// <summary>Enabled entries still below target; -1 when the list is missing or IPC failed.</summary>
    int PendingCount(string listName);

    VendorBuyListStartResult Start(string listName);

    void Stop();

    string StatusText();

    VendorBuyListLastRun LastRun();
}
