using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;

namespace BOCCHI.Common.Ipc.EurekaLinker;

public sealed class EurekaLinkerIpc : IEurekaLinkerIpc
{
    private readonly ICallGateSubscriber<int> apiVersion;
    private readonly ICallGateSubscriber<(uint, long, long, bool)[]> getTimers;

    public EurekaLinkerIpc(IDalamudPluginInterface pluginInterface)
    {
        apiVersion = pluginInterface.GetIpcSubscriber<int>("EurekaLinker.ApiVersion");
        getTimers = pluginInterface.GetIpcSubscriber<(uint, long, long, bool)[]>("EurekaLinker.Pot.GetTimers");
    }

    public bool IsAvailable
    {
        get
        {
            try
            {
                return apiVersion.HasFunction && getTimers.HasFunction;
            }
            catch
            {
                return false;
            }
        }
    }

    public bool TryGetPotTimers(out EurekaLinkerPotTimer[] timers)
    {
        timers = [];
        try
        {
            if (!getTimers.HasFunction)
            {
                return false;
            }

            (uint, long, long, bool)[] raw = getTimers.InvokeFunc();
            if (raw is not { Length: > 0 })
            {
                return false;
            }

            EurekaLinkerPotTimer[] mapped = new EurekaLinkerPotTimer[raw.Length];
            for (int i = 0; i < raw.Length; i++)
            {
                (uint fateId, long spawnUnix, long _, bool alive) = raw[i];
                mapped[i] = new EurekaLinkerPotTimer(fateId, spawnUnix, alive);
            }

            timers = mapped;
            return true;
        }
        catch
        {
            return false;
        }
    }
}
