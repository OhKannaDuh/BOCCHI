using Ocelot.Config;
using Ocelot.Config.Fields;

namespace BOCCHI.Common.Config;

[Serializable]
[ConfigGroup("automation", GroupOrder = 0, Order = 4)]
public class BuffConfig : IAutoConfig
{
    [Checkbox(Order = 0, Section = "automation")]
    public bool ShouldAutomateBuffs { get; set; } = false;

    [Checkbox(Order = 1, Indent = 1, Requires = nameof(ShouldAutomateBuffs), Section = "which")]
    public bool ApplyRomeosBallad { get; set; } = true;

    [Checkbox(Order = 2, Indent = 1, Requires = nameof(ShouldAutomateBuffs), Section = "which")]
    public bool ApplyEnduringFortitude { get; set; } = true;

    [Checkbox(Order = 3, Indent = 1, Requires = nameof(ShouldAutomateBuffs), Section = "which")]
    public bool ApplyFleetfooted { get; set; } = true;

    [Checkbox(Order = 4, Indent = 1, Requires = nameof(ShouldAutomateBuffs), Section = "which")]
    public bool ApplyQuickerStep { get; set; } = false;

    [Checkbox(Order = 5, Indent = 1, Requires = nameof(ShouldAutomateBuffs), Section = "which")]
    public bool ApplyBuffsUsingInquiringMind { get; set; } = true;

    [IntRange(0, 25, Order = 6, Indent = 1, Requires = nameof(ShouldAutomateBuffs), Section = "which")]
    public int ReapplyThreshold { get; set; } = 10;

    public bool ShouldApplyRomeosBallad() => ApplyRomeosBallad;

    public bool ShouldApplyEnduringFortitude() => ApplyEnduringFortitude;

    public bool ShouldApplyFleetfooted() => ApplyFleetfooted;

    public bool ShouldApplyQuickerStep() => ApplyQuickerStep;
}
