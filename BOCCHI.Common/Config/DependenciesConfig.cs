using BOCCHI.Common.Config.Fields;
using Ocelot.Config;

namespace BOCCHI.Common.Config;

[Serializable]
[ConfigGroup("dependencies", GroupOrder = 1000)]
public class DependenciesConfig : IAutoConfig
{
    [PluginDependencyStatus]
    public bool Status { get; set; }
}
