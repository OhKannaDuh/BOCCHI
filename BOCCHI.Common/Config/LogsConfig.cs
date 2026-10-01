using BOCCHI.Common.Config.Fields;
using Ocelot.Config;

namespace BOCCHI.Common.Config;

[Serializable]
[ConfigGroup("logs", GroupOrder = 950)]
public class LogsConfig : IAutoConfig
{
    [LogsViewer]
    public bool Viewer { get; set; }
}
