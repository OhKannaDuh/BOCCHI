using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Ocelot.Services.Logger;

namespace BOCCHI.Common.Services;

public sealed class YesAlreadyPause(IDalamudPluginInterface plugin, ILogger<YesAlreadyPause> logger)
{
    private readonly ICallGateSubscriber<bool> isEnabled = plugin.GetIpcSubscriber<bool>("YesAlready.IsPluginEnabled");

    private readonly ICallGateSubscriber<bool, object> setEnabled = plugin.GetIpcSubscriber<bool, object>("YesAlready.SetPluginEnabled");

    // Only true while YesAlready is off because BOCCHI turned it off; the user's own choice is never restored over.
    private bool pausedByBocchi;

    public void Hold()
    {
        if (pausedByBocchi)
        {
            return;
        }

        try
        {
            if (!isEnabled.HasFunction || !isEnabled.InvokeFunc())
            {
                return;
            }

            setEnabled.InvokeAction(false);
            pausedByBocchi = true;
            logger.Info("Auto revive: paused YesAlready until you are back up");
        }
        catch (Exception ex)
        {
            logger.Warning(ex, "Could not pause YesAlready");
        }
    }

    public void Release()
    {
        if (!pausedByBocchi)
        {
            return;
        }

        pausedByBocchi = false;
        try
        {
            if (isEnabled.HasFunction && !isEnabled.InvokeFunc())
            {
                setEnabled.InvokeAction(true);
                logger.Info("Auto revive: YesAlready is back on");
            }
        }
        catch (Exception ex)
        {
            logger.Warning(ex, "Could not turn YesAlready back on");
        }
    }
}
