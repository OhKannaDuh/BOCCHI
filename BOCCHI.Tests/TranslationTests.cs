using System.Reflection;
using System.Text.RegularExpressions;
using BOCCHI.Common.Config;
using System.Text.Json;
using Ocelot.Config;
using Ocelot.Config.Fields;
using Ocelot.Extensions;
using Xunit;

namespace BOCCHI.Tests;

public class TranslationTests
{
    private static readonly string[] Languages = ["en", "jp", "ko", "zh"];

    private static readonly Regex Placeholder = new(@"\{\w+(:[^}]*)?\}", RegexOptions.Compiled);

    private static readonly Lazy<Dictionary<string, Dictionary<string, string>>> Texts = new(() =>
        Languages.ToDictionary(language => language, Load));

    [Theory]
    [InlineData("jp")]
    [InlineData("ko")]
    [InlineData("zh")]
    public void EveryLanguageHasTheSameKeysAsEnglish(string language)
    {
        HashSet<string> english = Texts.Value["en"].Keys.ToHashSet();
        HashSet<string> other = Texts.Value[language].Keys.ToHashSet();

        Assert.True(english.Count > 500, $"only {english.Count} English texts were found");

        Assert.Empty(english.Except(other).OrderBy(k => k));
        Assert.Empty(other.Except(english).OrderBy(k => k));
    }

    [Theory]
    [InlineData("jp")]
    [InlineData("ko")]
    [InlineData("zh")]
    public void PlaceholdersMatchEnglish(string language)
    {
        List<string> wrong = [];
        foreach ((string key, string english) in Texts.Value["en"])
        {
            if (!Texts.Value[language].TryGetValue(key, out string? translated))
            {
                continue;
            }

            if (!Holes(english).SequenceEqual(Holes(translated)))
            {
                wrong.Add(key);
            }
        }

        Assert.Empty(wrong);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("jp")]
    [InlineData("ko")]
    [InlineData("zh")]
    public void NoTextIsEmpty(string language)
    {
        Assert.Empty(Texts.Value[language].Where(t => string.IsNullOrWhiteSpace(t.Value)).Select(t => t.Key));
    }

    [Fact]
    public void EverySettingHasALabelAndItsSectionHasAName()
    {
        List<string> missing = [];
        IEnumerable<Type> configs = typeof(FatesConfig).Assembly.GetTypes()
            .Where(t => typeof(IAutoConfig).IsAssignableFrom(t) && t is { IsClass: true, IsAbstract: false });

        foreach (Type config in configs)
        {
            string page = "config." + config.Name.Replace("Config", "").ToSnakeCase();
            if (!Texts.Value["en"].ContainsKey(page + ".label"))
            {
                missing.Add(page + ".label");
            }

            ConfigGroupAttribute? group = config.GetCustomAttribute<ConfigGroupAttribute>();
            if (group != null && !Texts.Value["en"].ContainsKey($"config_group.{group.Key}.label"))
            {
                missing.Add($"config_group.{group.Key}.label");
            }

            foreach (PropertyInfo property in config.GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                UIFieldAttribute? field = property.GetCustomAttributes().OfType<UIFieldAttribute>().SingleOrDefault();
                if (field == null)
                {
                    continue;
                }

                string prefix = $"{page}.fields.{property.Name.ToSnakeCase()}.";
                if (!Texts.Value["en"].ContainsKey(prefix + "label"))
                {
                    missing.Add(prefix + "label");
                }

                if (!string.IsNullOrEmpty(field.Section)
                    && !Texts.Value["en"].ContainsKey($"{page}.sections.{field.Section}"))
                {
                    missing.Add($"{page}.sections.{field.Section}");
                }

                if (!string.IsNullOrEmpty(field.Requires)
                    && config.GetProperty(field.Requires)?.PropertyType != typeof(bool))
                {
                    missing.Add($"{config.Name}.{property.Name}: Requires '{field.Requires}' is not a bool setting");
                }
            }
        }

        Assert.Empty(missing);
    }

    private static List<string> Holes(string text) =>
        Placeholder.Matches(text).Select(m => m.Value).OrderBy(v => v, StringComparer.Ordinal).ToList();

    private static Dictionary<string, string> Load(string language)
    {
        string root = RepositoryRoot();
        Dictionary<string, string> texts = new(StringComparer.Ordinal);
        foreach (string folder in new[] { Path.Combine(root, "Translations", language), Path.Combine(root, "Ocelot", "Translations", language) })
        {
            if (!Directory.Exists(folder))
            {
                continue;
            }

            foreach (string file in Directory.EnumerateFiles(folder, "*.json", SearchOption.AllDirectories))
            {
                string prefix = Path.GetRelativePath(folder, file)[..^".json".Length]
                    .Replace(Path.DirectorySeparatorChar, '.');
                if (prefix.StartsWith("ocelot.", StringComparison.Ordinal))
                {
                    prefix = prefix["ocelot.".Length..];
                }

                using FileStream stream = File.OpenRead(file);
                using JsonDocument document = JsonDocument.Parse(stream, new JsonDocumentOptions { AllowTrailingCommas = true });
                Flatten(document.RootElement, prefix, texts);
            }
        }

        return texts;
    }

    private static void Flatten(JsonElement token, string prefix, Dictionary<string, string> texts)
    {
        if (token.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in token.EnumerateObject())
            {
                Flatten(property.Value, $"{prefix}.{property.Name}", texts);
            }

            return;
        }

        texts.TryAdd(prefix, token.ToString());
    }

    private static string RepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "BOCCHI.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("BOCCHI.sln not found above the test folder");
    }
}
