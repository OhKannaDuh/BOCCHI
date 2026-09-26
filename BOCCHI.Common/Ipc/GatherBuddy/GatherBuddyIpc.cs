using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;

namespace BOCCHI.Common.Ipc.GatherBuddy;

/// <summary>
///     Typed client for GatherBuddy Reborn's EzIPC gates. Gate prefix is GBR's code-level
///     <c>InternalName</c> ("GatherBuddyReborn"), which fork builds keep even when their
///     manifest InternalName differs.
/// </summary>
public sealed class GatherBuddyIpc : IGatherBuddyIpc
{
    private const string Prefix = "GatherBuddyReborn.";

    /// <summary>First GBR IPC version with the vendor buy-list gates.</summary>
    public const int MinimumVersion = 3;

    private readonly ICallGateSubscriber<int> version;
    private readonly ICallGateSubscriber<string[]> listNames;
    private readonly ICallGateSubscriber<string, int> pendingCount;
    private readonly ICallGateSubscriber<string, int> start;
    private readonly ICallGateSubscriber<object> stop;
    private readonly ICallGateSubscriber<bool> isBusy;
    private readonly ICallGateSubscriber<string> statusText;
    private readonly ICallGateSubscriber<(int, bool)> lastRun;

    public GatherBuddyIpc(IDalamudPluginInterface pluginInterface)
    {
        version = pluginInterface.GetIpcSubscriber<int>(Prefix + "Version");
        listNames = pluginInterface.GetIpcSubscriber<string[]>(Prefix + "VendorBuyListNames");
        pendingCount = pluginInterface.GetIpcSubscriber<string, int>(Prefix + "VendorBuyListPendingCount");
        start = pluginInterface.GetIpcSubscriber<string, int>(Prefix + "VendorBuyListStart");
        stop = pluginInterface.GetIpcSubscriber<object>(Prefix + "VendorBuyListStop");
        isBusy = pluginInterface.GetIpcSubscriber<bool>(Prefix + "VendorBuyListIsBusy");
        statusText = pluginInterface.GetIpcSubscriber<string>(Prefix + "VendorBuyListStatusText");
        lastRun = pluginInterface.GetIpcSubscriber<(int, bool)>(Prefix + "VendorBuyListLastRun");
    }

    public int Version
    {
        get
        {
            try
            {
                return version.HasFunction ? version.InvokeFunc() : 0;
            }
            catch
            {
                return 0;
            }
        }
    }

    public bool IsAvailable => Version >= MinimumVersion;

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

    public IReadOnlyList<string> ListNames()
    {
        try
        {
            return listNames.HasFunction ? listNames.InvokeFunc() ?? [] : [];
        }
        catch
        {
            return [];
        }
    }

    public int PendingCount(string listName)
    {
        try
        {
            return pendingCount.HasFunction ? pendingCount.InvokeFunc(listName) : -1;
        }
        catch
        {
            return -1;
        }
    }

    public VendorBuyListStartResult Start(string listName)
    {
        try
        {
            return (VendorBuyListStartResult)start.InvokeFunc(listName);
        }
        catch
        {
            return VendorBuyListStartResult.IpcError;
        }
    }

    public void Stop()
    {
        try
        {
            if (stop.HasAction)
            {
                stop.InvokeAction();
            }
        }
        catch
        {
            // GBR unloaded — nothing left to stop.
        }
    }

    public string StatusText()
    {
        try
        {
            return statusText.HasFunction ? statusText.InvokeFunc() ?? string.Empty : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    public VendorBuyListLastRun LastRun()
    {
        try
        {
            if (!lastRun.HasFunction)
            {
                return default;
            }

            (int outcome, bool hitCurrencyLimit) = lastRun.InvokeFunc();
            return new VendorBuyListLastRun((VendorBuyListRunOutcome)outcome, hitCurrencyLimit);
        }
        catch
        {
            return default;
        }
    }
}
