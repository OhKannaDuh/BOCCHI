using System.Text.RegularExpressions;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;

namespace BOCCHI.Common.Data.Aethernet;

public static class ReturnYesNo
{
    private const uint PartyInviteAddonTextRow = 120;
    private const uint ReturnAddonTextRow = 197;

    private static string returnTemplate = string.Empty;
    private static string partyInviteTemplate = string.Empty;
    private static bool templatesLoaded;

    public static void Initialize(IDataManager data)
    {
        Addon? returnRow = data.GetExcelSheet<Addon>().GetRowOrDefault(ReturnAddonTextRow);
        Addon? partyRow = data.GetExcelSheet<Addon>().GetRowOrDefault(PartyInviteAddonTextRow);
        returnTemplate = returnRow?.Text.ToString().Trim() ?? string.Empty;
        partyInviteTemplate = partyRow?.Text.ToString().Trim() ?? string.Empty;
        templatesLoaded = true;
    }

    public static unsafe bool IsReturnConfirmation(AtkUnitBase* addon)
    {
        if (addon == null || !addon->IsVisible)
        {
            return false;
        }

        string prompt = ReadPrompt(addon);
        if (prompt.Length == 0)
        {
            return false;
        }

        if (templatesLoaded && returnTemplate.Length > 0 && MatchesLocalizedPrompt(prompt, returnTemplate))
        {
            return true;
        }

        if (templatesLoaded && partyInviteTemplate.Length > 0 && MatchesLocalizedPrompt(prompt, partyInviteTemplate))
        {
            return false;
        }

        if (addon->AtkValuesCount > 7
            && addon->AtkValues[7].Type == AtkValueType.Int
            && addon->AtkValues[7].Int == -1
            && LooksLikeReturnPrompt(prompt))
        {
            return true;
        }

        return false;
    }

    public static unsafe bool TryAccept(AtkUnitBase* addon)
    {
        if (addon == null || !addon->IsVisible)
        {
            return false;
        }

        if (!IsReturnConfirmation(addon))
        {
            return false;
        }

        addon->FireCallbackInt(0);
        return true;
    }

    private static unsafe string ReadPrompt(AtkUnitBase* addon)
    {
        AddonSelectYesno* yesno = (AddonSelectYesno*)addon;
        if (yesno->PromptText != null)
        {
            string fromNode = yesno->PromptText->NodeText.ToString().Trim();
            if (fromNode.Length > 0)
            {
                return fromNode;
            }
        }

        return string.Empty;
    }

    private static bool LooksLikeReturnPrompt(string prompt)
    {
        string n = NormalizePrompt(prompt);
        return n.Contains("return", StringComparison.OrdinalIgnoreCase)
               || n.Contains("帰還", StringComparison.Ordinal)
               || n.Contains("リターン", StringComparison.Ordinal);
    }

    private static bool MatchesLocalizedPrompt(string prompt, string template)
    {
        string normalizedPrompt = NormalizePrompt(prompt);
        string normalizedTemplate = NormalizePrompt(template);
        if (normalizedPrompt.Length == 0 || normalizedTemplate.Length == 0)
        {
            return false;
        }

        string[] templateParts = Regex.Split(normalizedTemplate, @"<string\([^>]+\)>", RegexOptions.IgnoreCase);
        if (templateParts.Length == 1)
        {
            if (string.Equals(normalizedPrompt, normalizedTemplate, StringComparison.CurrentCultureIgnoreCase))
            {
                return true;
            }

            return MatchesPromptWithInsertedText(normalizedPrompt, normalizedTemplate);
        }

        string pattern = "^" + string.Join(".+?", templateParts.Select(Regex.Escape)) + "$";
        return Regex.IsMatch(normalizedPrompt, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
               || MatchesPromptWithInsertedText(normalizedPrompt, normalizedTemplate);
    }

    private static bool MatchesPromptWithInsertedText(string prompt, string template)
    {
        if (prompt.Length <= template.Length)
        {
            return false;
        }

        int prefixLength = 0;
        while (prefixLength < template.Length
               && prefixLength < prompt.Length
               && CharEqualsIgnoreCase(prompt[prefixLength], template[prefixLength]))
        {
            prefixLength++;
        }

        int suffixLength = 0;
        while (suffixLength < template.Length - prefixLength
               && suffixLength < prompt.Length - prefixLength
               && CharEqualsIgnoreCase(
                   prompt[prompt.Length - suffixLength - 1],
                   template[template.Length - suffixLength - 1]))
        {
            suffixLength++;
        }

        return prefixLength + suffixLength == template.Length;
    }

    private static bool CharEqualsIgnoreCase(char left, char right)
        => char.ToUpperInvariant(left) == char.ToUpperInvariant(right);

    private static string NormalizePrompt(string value)
    {
        string withoutLineBreakTags = Regex.Replace(value, @"<br\s*/?>", " ", RegexOptions.IgnoreCase);
        return string.Join(" ", withoutLineBreakTags.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }
}
