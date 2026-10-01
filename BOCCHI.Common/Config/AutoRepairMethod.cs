using Ocelot.Config.Renderers.Enum;

namespace BOCCHI.Common.Config;

public enum AutoRepairMethod
{
    SelfRepair = 0,

    MenderNpc = 1,

    PreferMender = 2,
}

public sealed class AutoRepairMethodDisplay : IEnumDisplay<AutoRepairMethod>
{
    public string Display(AutoRepairMethod value) => value switch
    {
        AutoRepairMethod.MenderNpc => "Mender NPC",
        AutoRepairMethod.PreferMender => "Prefer mender NPC",
        _ => "Self-repair",
    };
}
