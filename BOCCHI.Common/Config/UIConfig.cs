using Ocelot.Config;
using Ocelot.Config.Fields;
using Ocelot.Config.Renderers.Enum;

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
        UILanguage.Japanese => "日本語",
        UILanguage.Korean => "한국어",
        UILanguage.ChineseSimplified => "简体中文",
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

    [Checkbox(Order = 2, Section = "trackers")]
    public bool ShowExperienceTracker { get; set; } = true;

    [Checkbox(Order = 3, Section = "trackers")]
    public bool ShowExperienceTrackerGraph { get; set; } = false;

    [Checkbox(Order = 4, Section = "trackers")]
    public bool ShowCurrencyTracker { get; set; } = true;

    [Checkbox(Order = 5, Section = "trackers")]
    public bool ShowCurrencyTrackerGraph { get; set; } = false;

    [Checkbox(Order = 6, Section = "trackers")]
    public bool ShowActiveTreasureTracker { get; set; } = true;

    [Checkbox(Order = 7, Section = "events")]
    public bool ShowDemiatmaDrops { get; set; } = true;

    [Checkbox(Order = 8, Section = "events")]
    public bool ShowNoteDrops { get; set; } = true;

    [Checkbox(Order = 9, Section = "events")]
    public bool ShowSoulShardDrops { get; set; } = true;

    [Checkbox(Order = 10, Section = "panels")]
    public bool ShowWorldSection { get; set; } = true;

    [Checkbox(Order = 11, Section = "panels")]
    public bool ShowBuffSection { get; set; } = true;

    [Checkbox(Order = 12, Section = "panels")]
    public bool ShowAutomationSection { get; set; } = true;

    [Checkbox(Order = 13, Section = "panels")]
    public bool ShowCompletionistSection { get; set; } = true;

    [Checkbox(Order = 14, Section = "panels")]
    public bool ShowMobFarmerSection { get; set; } = true;

    [Checkbox(Order = 15, Section = "panels")]
    public bool ShowPotsTreasureSection { get; set; } = true;

    [Checkbox(Order = 16, Section = "panels")]
    public bool ShowTreasureSection { get; set; } = true;

    [Checkbox(Order = 17, Section = "chat")]
    public bool ShowBocchiChatPrefix { get; set; } = true;

    public bool AnyEventDropsEnabled => ShowDemiatmaDrops || ShowNoteDrops || ShowSoulShardDrops;
}
