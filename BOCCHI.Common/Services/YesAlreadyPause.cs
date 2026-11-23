using Dalamud.Plugin;
using Ocelot.Services.Logger;

namespace BOCCHI.Common.Services;

public sealed class YesAlreadyPause(IDalamudPluginInterface plugin, ILogger<YesAlreadyPause> logger) : IDisposable
{
    // YesAlready answers nothing while any name is in this shared set; it only lives in memory, so it can't outlast the game.
    private const string StopRequestsKey = "YesAlready.StopRequests";

    private const string Requester = "BOCCHI";

    private HashSet<string>? stopRequests;

    private bool held;

    public void Hold()
    {
        if (held)
        {
            return;
        }

        held = true;
        try
        {
            stopRequests ??= plugin.GetOrCreateData(StopRequestsKey, () => new HashSet<string>());
            lock (stopRequests)
            {
                stopRequests.Add(Requester);
            }

            logger.Info("Auto revive: paused YesAlready until you are back up");
        }
        catch (Exception ex)
        {
            logger.Warning(ex, "Could not pause YesAlready");
        }
    }

    public void Release()
    {
        if (!held)
        {
            return;
        }

        held = false;
        if (stopRequests == null)
        {
            return;
        }

        lock (stopRequests)
        {
            stopRequests.Remove(Requester);
        }

        logger.Info("Auto revive: YesAlready is back on");
    }

    public void Dispose()
    {
        Release();
        if (stopRequests != null)
        {
            plugin.RelinquishData(StopRequestsKey);
            stopRequests = null;
        }
    }
}
