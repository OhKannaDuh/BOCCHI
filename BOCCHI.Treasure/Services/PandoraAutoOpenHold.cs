using Ocelot.Ipc.PandorasBox;
using Ocelot.Lifecycle;
using Ocelot.Services.Logger;

namespace BOCCHI.Treasure.Services;

public sealed class PandoraAutoOpenHold(IPandorasBoxIpc pandora, ILogger<PandoraAutoOpenHold> log)
    : IOnStop
{
    public const string AutoOpenChestsInternalName = "AutoOpenChests";

    private int holds;

    private bool? wasEnabled;

    public void Hold()
    {
        if (holds++ > 0)
        {
            return;
        }

        if (!pandora.IsAvailable)
        {
            return;
        }

        wasEnabled = pandora.GetFeatureEnabledInternal(AutoOpenChestsInternalName);
        if (wasEnabled != true)
        {
            return;
        }

        pandora.SetFeatureEnabledInternal(AutoOpenChestsInternalName, false);
        log.Info("Paused Pandora AutoOpenChests while BOCCHI opens coffers");
    }

    public void Release()
    {
        if (holds <= 0)
        {
            return;
        }

        if (--holds > 0)
        {
            return;
        }

        if (wasEnabled == true && pandora.IsAvailable)
        {
            pandora.SetFeatureEnabledInternal(AutoOpenChestsInternalName, true);
            log.Info("Restored Pandora AutoOpenChests");
        }

        wasEnabled = null;
    }

    public void OnStop()
    {
        while (holds > 0)
        {
            Release();
        }
    }
}
