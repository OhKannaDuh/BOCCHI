namespace BOCCHI.Common.Ipc.Knightshopper;

public interface IKnightshopperIpc
{
    bool IsAvailable { get; }

    bool IsBusy { get; }

    StartResponse Start(CurrencyId currency);

    PurchaseStatus GetStatus(Guid operationId);

    bool IsRunning(Guid operationId);

    bool Cancel(Guid operationId);

    event Action<PurchaseFinished>? Finished;
}
