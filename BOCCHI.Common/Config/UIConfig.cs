using Newtonsoft.Json;
using Ocelot.Config;
using Ocelot.Config.Fields;
using Ocelot.Config.Renderers.Enum;
using Ocelot.UI;

namespace BOCCHI.Common.Config;

public enum UILanguage
{
    English,
    Japanese,
    Korean,
    ChineseSimplified
}

public static class UILanguageExtensions
{
    extension(UILanguage language)
    {
        public string TranslationCode() => language switch
        {
            UILanguage.Japanese => "jp",
            UILanguage.Korean => "ko",
            UILanguage.ChineseSimplified => "zh",
            _ => "en"
        };
    }
}

public class UILanguageDisplay : IEnumDisplay<UILanguage>
{
    public string Display(UILanguage value) => value switch
    {
        UILanguage.Japanese => "Japanese",
        UILanguage.Korean => "Korean",
        UILanguage.ChineseSimplified => "Chinese (Simplified)",
        _ => "English"
    };
}

[Serializable]
[ConfigGroup("ux", GroupOrder = 30, Order = 0)]
public class UIConfig : IAutoConfig
{
    [EnumSelectDisplay<UILanguage, UILanguageDisplay>(Order = 0, Section = "general")]
    public UILanguage Language { get; set; } = UILanguage.English;

    [Checkbox(Order = 1, Section = "general")]
    public bool OpenOnOccultCrescentEntry { get; set; } = false;

    [Checkbox(Order = 2, Section = "general")]
    public bool UseBocchiTheme
    {
        get => OcelotTheme.Enabled;
        set => OcelotTheme.Enabled = value;
    }

    [Checkbox(Order = 3, Section = "trackers")]
    public bool ShowExperienceTracker { get; set; } = true;

    [JsonIgnore]
    public bool ShowExperienceTrackerGraph => ShowTrackerGraphs;

    [Checkbox(Order = 5, Section = "trackers")]
    public bool ShowCurrencyTracker { get; set; } = true;

    [Checkbox(Order = 6, Section = "trackers")]
    public bool ShowTrackerGraphs { get; set; } = false;

    [JsonIgnore]
    public bool ShowCurrencyTrackerGraph => ShowTrackerGraphs;

    [Checkbox(Order = 7, Section = "trackers")]
    public bool ShowActiveTreasureTracker { get; set; } = true;

    [Checkbox(Order = 8, Section = "events")]
    public bool ShowRewardIcons { get; set; } = true;

    [JsonIgnore]
    public bool ShowDemiatmaDrops => ShowRewardIcons;

    [JsonIgnore]
    public bool ShowNoteDrops => ShowRewardIcons;

    [JsonIgnore]
    public bool ShowSoulShardDrops => ShowRewardIcons;

    [Checkbox(Order = 2, Section = "general")]
    public bool ShowBuffSection { get; set; } = true;

    [Checkbox(Order = 18, Section = "chat")]
    public bool ShowBocchiChatPrefix { get; set; } = true;

    public bool AnyEventDropsEnabled => ShowDemiatmaDrops || ShowNoteDrops || ShowSoulShardDrops;
}
