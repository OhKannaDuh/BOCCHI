using BOCCHI.Common.Config.Fields;
using Ocelot.Config;
using Ocelot.Config.Fields;

namespace BOCCHI.Common.Config;

[Serializable]
[ConfigGroup("raise", GroupOrder = 15)]
public class RaiseConfig : IAutoConfig
{
    [Checkbox(Order = 0)]
    public bool AutoAcceptRaise { get; set; } = false;

    [Checkbox(Order = 1, Indent = 1, Requires = nameof(AutoAcceptRaise))]
    public bool SayThanksAfterRaise { get; set; } = false;

    [ChatMessage(Order = 2, Indent = 2, Requires = nameof(SayThanksAfterRaise))]
    public string ThanksMessage { get; set; } = "Thank you!";
}
