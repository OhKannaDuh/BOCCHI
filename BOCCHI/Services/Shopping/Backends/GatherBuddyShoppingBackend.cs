using BOCCHI.Common.Config;
using BOCCHI.Common.Ipc.GatherBuddy;

namespace BOCCHI.Services.Shopping.Backends;

/// <summary>GatherBuddy Reborn vendor buy list named in <see cref="ShoppingConfig.GatherBuddyListName"/>.</summary>
public sealed class GatherBuddyShoppingBackend(IGatherBuddyIpc gatherBuddy, ShoppingConfig config) : IShoppingBackend
{
    /// <summary>List captured at start so a mid-run config edit does not change what is reported.</summary>
    private string? runningList;

    public ShoppingBackendKind Kind => ShoppingBackendKind.GatherBuddyReborn;

    public string Name => "GatherBuddy Reborn";

    public bool IsAvailable => gatherBuddy.IsAvailable;

    public bool HasRun => runningList is not null;

    private string ListName => config.GatherBuddyListName?.Trim() ?? string.Empty;

    private static string ListLabel(string list) => list.Length == 0 ? "active list" : $"list '{list}'";

    public ShoppingReadiness CheckReady(out string reason)
    {
        if (!gatherBuddy.IsAvailable)
        {
            int version = gatherBuddy.Version;
            reason = version > 0
                ? $"GatherBuddy Reborn IPC v{version} is too old (need v{GatherBuddyIpc.MinimumVersion}+)."
                : "GatherBuddy Reborn isn’t loaded.";
            return ShoppingReadiness.NotReady;
        }

        if (gatherBuddy.IsBusy)
        {
            reason = "GatherBuddy Reborn is already running a vendor list.";
            return ShoppingReadiness.Busy;
        }

        string list = ListName;
        int pending = gatherBuddy.PendingCount(list);
        if (pending < 0)
        {
            reason = $"GatherBuddy Reborn has no {ListLabel(list)} (Vendors tab).";
            return ShoppingReadiness.NotReady;
        }

        if (pending == 0)
        {
            reason = $"GatherBuddy Reborn {ListLabel(list)} has nothing left to buy.";
            return ShoppingReadiness.NotReady;
        }

        reason = string.Empty;
        return ShoppingReadiness.Ready;
    }

    public ShoppingStart TryStart()
    {
        string list = ListName;
        VendorBuyListStartResult result = gatherBuddy.Start(list);
        string message = $"{result} — {gatherBuddy.StatusText()}";

        switch (result)
        {
            case VendorBuyListStartResult.Started:
            case VendorBuyListStartResult.WaitingForPreviousInteraction:
                runningList = list;
                return new ShoppingStart(ShoppingStartKind.Started, message);
            case VendorBuyListStartResult.AlreadyRunning:
            case VendorBuyListStartResult.VendorDataLoading:
            case VendorBuyListStartResult.LocationDataLoading:
            case VendorBuyListStartResult.AnotherPurchaseRunning:
                return new ShoppingStart(ShoppingStartKind.Retry, message);
            default:
                return new ShoppingStart(ShoppingStartKind.Rejected, message);
        }
    }

    public ShoppingPoll Poll()
    {
        if (runningList is null)
        {
            return new ShoppingPoll(true, false, string.Empty);
        }

        if (gatherBuddy.IsBusy)
        {
            return new ShoppingPoll(false, false, gatherBuddy.StatusText());
        }

        runningList = null;
        VendorBuyListLastRun last = gatherBuddy.LastRun();
        return new ShoppingPoll(true, last.HitCurrencyLimit, $"{last.Outcome} — {gatherBuddy.StatusText()}");
    }

    public void Cancel()
    {
        if (runningList is not null)
        {
            gatherBuddy.Stop();
        }

        runningList = null;
    }

    public void Forget() => runningList = null;

    public string DescribeStatus()
    {
        string head = runningList is { } list
            ? $"GBR {ListLabel(list)} running"
            : $"GBR available={gatherBuddy.IsAvailable} busy={gatherBuddy.IsBusy} {ListLabel(ListName)}";
        string status = gatherBuddy.StatusText();
        return status.Length == 0 ? head : $"{head} — {status}";
    }
}
