using System.Text.RegularExpressions;

namespace BOCCHI.Common.Services;

/// <summary>Matches SelectYesno prompt text against localized Lumina <c>Addon</c> templates.</summary>
public static class SelectYesnoPrompt
{
    /// <summary>
    /// True when <paramref name="prompt"/> is <paramref name="template"/> with its placeholders filled in.
    /// Handles both macro templates (<c>&lt;string(lstr1)&gt;</c>) and extracted text where the placeholder is gone.
    /// </summary>
    public static bool Matches(string prompt, string template)
    {
        string normalizedPrompt = Normalize(prompt);
        string normalizedTemplate = Normalize(template);
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

            return MatchesWithInsertedText(normalizedPrompt, normalizedTemplate);
        }

        string pattern = "^" + string.Join(".+?", templateParts.Select(Regex.Escape)) + "$";
        return Regex.IsMatch(normalizedPrompt, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
               || MatchesWithInsertedText(normalizedPrompt, normalizedTemplate);
    }

    public static string Normalize(string value)
    {
        string withoutLineBreakTags = Regex.Replace(value, @"<br\s*/?>", " ", RegexOptions.IgnoreCase);
        return string.Join(" ", withoutLineBreakTags.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static bool MatchesWithInsertedText(string prompt, string template)
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
}
