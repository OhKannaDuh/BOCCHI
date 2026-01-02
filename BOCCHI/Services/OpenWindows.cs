using Ocelot.Lifecycle;
using Ocelot.Windows;

namespace BOCCHI.Services;

public class OpenWindows(IMainWindow? main, IConfigWindow? config) : IOnStart
{
    public void OnStart()
    {
        main?.IsOpen = true;
        // config?.IsOpen = true;
    }
}
