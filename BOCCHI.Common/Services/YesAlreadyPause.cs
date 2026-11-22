using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Ocelot.Services.Logger;

namespace BOCCHI.Common.Services;

public sealed class YesAlreadyPause(IDalamudPluginInterface plugin, ILogger<YesAlreadyPause> logger)
{
    private readonly ICallGateSubscriber<bool> isEnabled = plugin.GetIpcSubscriber<bool>("YesAlready.IsPluginEnabled");

    private readonly ICallGateSubscriber<bool, object> setEnabled = plugin.GetIpcSubscriber<bool, object>("YesAlready.SetPluginEnabled");

    private bool pausedByBocchi;

    private DateTime nextAskUtc;

    public void Hold() => Apply(wantPause: true);

    public void Release() => Apply(wantPause: false);

    // Only turn YesAlready back on if BOCCHI turned it off; a user who switches it themselves is left alone.
    public static (bool? SetEnabled, bool PausedByBocchi) Decide(bool wantPause, bool isEnabledNow, bool pausedByBocchi)
    {
        if (wantPause)
        {
            return isEnabledNow && !pausedByBocchi ? (false, true) : (null, pausedByBocchi);
        }

        return pausedByBocchi && !isEnabledNow ? (true, false) : (null, false);
    }

    private void Apply(bool wantPause)
    {
        if (wantPause == pausedByBocchi)
        {
            return;
        }

        // With YesAlready switched off by the user there is nothing to pause; don't ask it every frame.
        if (wantPause && DateTime.UtcNow < nextAskUtc)
        {
            return;
        }

        nextAskUtc = DateTime.UtcNow.AddSeconds(1);

        try
        {
            if (!isEnabled.HasFunction)
            {
                pausedByBocchi = false;
                return;
            }

            (bool? set, pausedByBocchi) = Decide(wantPause, isEnabled.InvokeFunc(), pausedByBocchi);
            if (set is { } enabled)
            {
                setEnabled.InvokeAction(enabled);
                logger.Info(enabled ? "Auto revive: YesAlready is back on" : "Auto revive: paused YesAlready until you are back up");
            }
        }
        catch (Exception ex)
        {
            logger.Warning(ex, "Could not switch YesAlready");
        }
    }
}
