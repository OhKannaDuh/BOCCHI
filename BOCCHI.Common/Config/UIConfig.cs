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
    [EnumSelectDisplay<UILanguage, UILanguageDisplay>]
    public UILanguage Language { get; set; } = UILanguage.English;

    [Checkbox] public bool OpenOnOccultCrescentEntry { get; set; } = false;

    [Checkbox] public bool ShowExperienceTracker { get; set; } = true;

    [Checkbox] public bool ShowExperienceTrackerGraph { get; set; } = false;

    [Checkbox] public bool ShowCurrencyTracker { get; set; } = true;

    [Checkbox] public bool ShowCurrencyTrackerGraph { get; set; } = false;

    [Checkbox] public bool ShowWorldSection { get; set; } = true;

    [Checkbox] public bool ShowBuffSection { get; set; } = true;

    [Checkbox] public bool ShowAutomationSection { get; set; } = true;

    [Checkbox] public bool ShowMobFarmerSection { get; set; } = true;

    [Checkbox] public bool ShowPotsTreasureSection { get; set; } = true;

    [Checkbox] public bool ShowTreasureSection { get; set; } = true;

    /// <summary>Print plugin chat notifications (always with [BOCCHI] when enabled).</summary>
    [Checkbox]
    public bool ShowBocchiChatPrefix { get; set; } = true;
}
