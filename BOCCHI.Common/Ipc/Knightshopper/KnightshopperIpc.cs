using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Ocelot.Lifecycle;

namespace BOCCHI.Common.Ipc.Knightshopper;

/// <summary>
/// Typed client adapted from Knightshopper's shipped
/// <c>docs/KnightshopperIpc.cs</c> (Purchase.* gates).
/// </summary>
public sealed class KnightshopperIpc : IKnightshopperIpc, IOnStop, IDisposable
{
    private readonly ICallGateSubscriber<int> apiVersion;
    private readonly ICallGateSubscriber<bool> isBusy;
    private readonly ICallGateSubscriber<int, (int, Guid, string)> start;
    private readonly ICallGateSubscriber<Guid, (int, int, int, uint, string)> getStatus;
    private readonly ICallGateSubscriber<Guid, bool> isRunning;
    private readonly ICallGateSubscriber<Guid, bool> cancel;
    private readonly ICallGateSubscriber<Guid, int, string, object> finished;
    private bool disposed;

    public KnightshopperIpc(IDalamudPluginInterface pluginInterface)
    {
        apiVersion = pluginInterface.GetIpcSubscriber<int>("Knightshopper.ApiVersion");
        isBusy = pluginInterface.GetIpcSubscriber<bool>("Knightshopper.IsBusy");
        start = pluginInterface.GetIpcSubscriber<int, (int, Guid, string)>("Knightshopper.Purchase.Start");
        getStatus = pluginInterface.GetIpcSubscriber<Guid, (int, int, int, uint, string)>("Knightshopper.Purchase.GetStatus");
        isRunning = pluginInterface.GetIpcSubscriber<Guid, bool>("Knightshopper.Purchase.IsRunning");
        cancel = pluginInterface.GetIpcSubscriber<Guid, bool>("Knightshopper.Purchase.Cancel");
        finished = pluginInterface.GetIpcSubscriber<Guid, int, string, object>("Knightshopper.Purchase.Finished");
        finished.Subscribe(OnFinished);
    }

    public event Action<PurchaseFinished>? Finished;

    public bool IsAvailable
    {
        get
        {
            try
            {
                return apiVersion.HasFunction;
            }
            catch
            {
                return false;
            }
        }
    }

    public bool IsBusy
    {
        get
        {
            try
            {
                return isBusy.HasFunction && isBusy.InvokeFunc();
            }
            catch
            {
                return false;
            }
        }
    }

    public StartResponse Start(CurrencyId currency)
    {
        try
        {
            (int, Guid, string) response = start.InvokeFunc((int)currency);
            return new StartResponse((StartResult)response.Item1, response.Item2, response.Item3);
        }
        catch (Exception)
        {
            return new StartResponse(StartResult.NotReady, Guid.Empty, "Knightshopper.Start failed");
        }
    }

    public PurchaseStatus GetStatus(Guid operationId)
    {
        try
        {
            (int, int, int, uint, string) status = getStatus.InvokeFunc(operationId);
            return new PurchaseStatus(
                (PurchaseState)status.Item1,
                status.Item2,
                status.Item3,
                status.Item4,
                status.Item5);
        }
        catch
        {
            return new PurchaseStatus(PurchaseState.Unknown, 0, 0, 0, string.Empty);
        }
    }

    public bool IsRunning(Guid operationId)
    {
        try
        {
            return isRunning.HasFunction && isRunning.InvokeFunc(operationId);
        }
        catch
        {
            return false;
        }
    }

    public bool Cancel(Guid operationId)
    {
        try
        {
            return cancel.HasFunction && cancel.InvokeFunc(operationId);
        }
        catch
        {
            return false;
        }
    }

    private void OnFinished(Guid operationId, int state, string message) =>
        Finished?.Invoke(new PurchaseFinished(operationId, (PurchaseState)state, message));

    public void OnStop() => Dispose();

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        try
        {
            finished.Unsubscribe(OnFinished);
        }
        catch
        {
            // Provider may already be gone if Knightshopper unloaded first.
        }
    }
}
