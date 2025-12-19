using Ocelot.Config.Attributes;
using Ocelot.Modules;

namespace BOCCHI.Modules.Buff;

public class BuffConfig : ModuleConfig
{
    [Checkbox]
    [IllegalModeCompatible]
    [Label("generic.label.enabled")]
    public bool Enabled { get; set; } = true;

    [Checkbox] [IllegalModeCompatible] public bool ApplyRomeosBallad { get; set; } = true;

    [Checkbox] [IllegalModeCompatible] public bool ApplyEnduringFortitude { get; set; } = true;

    [Checkbox] [IllegalModeCompatible] public bool ApplyFleetfooted { get; set; } = true;
    
    [Checkbox] [IllegalModeCompatible] public bool ApplyQuickerStep { get; set; } = false;

    [IntRange(0, 25)]
    [IllegalModeCompatible]
    public int ReapplyThreshold { get; set; } = 10;
}
