namespace BOCCHI.Common.Ipc.GatherBuddy;

/// <summary>Mirrors GBR <c>VendorBuyListManager.StartResult</c>; <see cref="ListNotFound"/> is IPC-only.</summary>
public enum VendorBuyListStartResult
{
    ListNotFound = -1,
    Started = 0,
    AlreadyRunning = 1,
    AutomationUnavailable = 2,
    WaitingForPreviousInteraction = 3,
    NoList = 4,
    Empty = 5,
    NoPendingEntries = 6,
    VendorDataLoading = 7,
    LocationDataLoading = 8,
    AnotherPurchaseRunning = 9,

    /// <summary>BOCCHI-side: the IPC call threw (GBR unloaded mid-call, signature mismatch).</summary>
    IpcError = int.MinValue,
}

/// <summary>Mirrors GBR <c>VendorBuyListManager.RunOutcome</c>.</summary>
public enum VendorBuyListRunOutcome
{
    None = 0,
    Completed = 1,
    CompletedWithIssues = 2,
    Failed = 3,
    Stopped = 4,
}

public readonly record struct VendorBuyListLastRun(VendorBuyListRunOutcome Outcome, bool HitCurrencyLimit);
