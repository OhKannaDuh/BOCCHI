using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace BOCCHI.Services.Changelog;

public static class ChangelogText
{
    private const string ResourceName = "BOCCHI.CHANGELOG.md";

    private static readonly Regex Bold = new(@"\*\*(.+?)\*\*", RegexOptions.Compiled);

    public static string CurrentPluginVersion =>
        typeof(Plugin).Assembly.GetName().Version?.ToString() ?? "0.0.0.0";

    public static bool TryGetSectionForVersion(string version, out string body)
    {
        body = string.Empty;
        if (string.IsNullOrWhiteSpace(version))
        {
            return false;
        }

        string? markdown = ReadEmbeddedChangelog();
        if (markdown is null)
        {
            return false;
        }

        string? section = ExtractVersionSection(markdown, version.Trim());
        if (section is null || !HasUserFacingNotes(section))
        {
            return false;
        }

        body = section.Trim();
        return true;
    }

    public static IReadOnlyList<ChangelogLine> ParseLines(string section)
    {
        List<ChangelogLine> lines = [];
        string? pendingHeading = null;
        foreach (string raw in section.Split('\n'))
        {
            string line = raw.TrimEnd('\r').TrimEnd();
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (line.StartsWith("### ", StringComparison.Ordinal))
            {
                pendingHeading = StripMarkdown(line[4..].Trim());
                continue;
            }

            if (line.StartsWith("- ", StringComparison.Ordinal))
            {
                if (pendingHeading is not null)
                {
                    lines.Add(new ChangelogLine(ChangelogLineKind.Heading, pendingHeading));
                    pendingHeading = null;
                }

                lines.Add(new ChangelogLine(ChangelogLineKind.Bullet, StripMarkdown(line[2..].Trim())));
                continue;
            }

            if (line.StartsWith("# ", StringComparison.Ordinal))
            {
                continue;
            }

            if (pendingHeading is not null)
            {
                lines.Add(new ChangelogLine(ChangelogLineKind.Heading, pendingHeading));
                pendingHeading = null;
            }

            lines.Add(new ChangelogLine(ChangelogLineKind.Paragraph, StripMarkdown(line.Trim())));
        }

        return lines;
    }

    private static string? ReadEmbeddedChangelog()
    {
        Assembly assembly = typeof(Plugin).Assembly;
        using Stream? stream = assembly.GetManifestResourceStream(ResourceName);
        if (stream is null)
        {
            return null;
        }

        using StreamReader reader = new(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static string? ExtractVersionSection(string markdown, string version)
    {
        string heading = $"# {version}";
        string[] lines = markdown.Replace("\r\n", "\n").Split('\n');
        int start = -1;
        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].Trim() == heading)
            {
                start = i + 1;
                break;
            }
        }

        if (start < 0)
        {
            return null;
        }

        StringBuilder sb = new();
        for (int i = start; i < lines.Length; i++)
        {
            string trimmed = lines[i].Trim();
            if (trimmed.StartsWith("# ", StringComparison.Ordinal) && !trimmed.StartsWith("### ", StringComparison.Ordinal))
            {
                break;
            }

            sb.AppendLine(lines[i]);
        }

        return sb.ToString();
    }

    private static bool HasUserFacingNotes(string section)
    {
        foreach (string raw in section.Split('\n'))
        {
            if (raw.TrimStart().StartsWith("- ", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static string StripMarkdown(string text) => Bold.Replace(text, "$1");
}

public enum ChangelogLineKind
{
    Heading,
    Bullet,
    Paragraph,
}

public readonly record struct ChangelogLine(ChangelogLineKind Kind, string Text);
