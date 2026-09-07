namespace BOCCHI.Common.Ipc.Knightshopper;

public enum CurrencyId
{
    BicolorGemstone = 0,
    CompanySeal = 1,
    Gil = 2,
    Hunt = 3,
    MGP = 4,
    PVP = 5,
    Scrip = 6,
    Tomestone = 7,
    Firmament = 8,
    Cosmocredits = 9,
    OccultCrescent = 10,
}

public enum StartResult
{
    Started = 0,
    Busy = 1,
    InvalidCurrency = 2,
    EmptyList = 3,
    NotReady = 4,
    NotLoggedIn = 5,
}

public enum PurchaseState
{
    Unknown = 0,
    Running = 1,
    CancellationRequested = 2,
    Succeeded = 3,
    Failed = 4,
    Cancelled = 5,
}

public readonly record struct StartResponse(StartResult Result, Guid OperationId, string Message)
{
    public bool Started => Result == StartResult.Started;
}

public readonly record struct PurchaseStatus(
    PurchaseState State,
    int CurrentIndex,
    int TotalItems,
    uint CurrentItemId,
    string Message)
{
    public bool IsFinished => State is PurchaseState.Succeeded or PurchaseState.Failed or PurchaseState.Cancelled;
}

public readonly record struct PurchaseFinished(Guid OperationId, PurchaseState State, string Message);
