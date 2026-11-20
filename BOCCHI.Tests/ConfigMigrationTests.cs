using System.Text.RegularExpressions;
using BOCCHI.Common.Config.Migrations;
using Newtonsoft.Json.Linq;
using Xunit;

namespace BOCCHI.Tests;

public class ConfigMigrationTests
{
    private static readonly IMigrator[] Migrators = typeof(IMigrator).Assembly
        .GetTypes()
        .Where(t => typeof(IMigrator).IsAssignableFrom(t) && t is { IsClass: true, IsAbstract: false })
        .Select(t => (IMigrator)Activator.CreateInstance(t)!)
        .OrderBy(m => m.FromVersion)
        .ToArray();

    private static readonly int CurrentVersion =
        int.Parse(Regex.Match(RepoFile("BOCCHI", "Config", "Configuration.cs"), @"CurrentVersion\s*=\s*(\d+)").Groups[1].Value);

    private static string RepoFile(params string[] parts)
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "BOCCHI.sln")))
        {
            dir = dir.Parent;
        }

        return File.ReadAllText(Path.Combine([dir!.FullName, .. parts]));
    }

    [Fact]
    public void Every_version_has_one_step_up_to_current()
    {
        Assert.Equal(Enumerable.Range(1, CurrentVersion - 1), Migrators.Select(m => m.FromVersion));
        Assert.All(Migrators, m => Assert.Equal(m.FromVersion + 1, m.ToVersion));
    }

    [Fact]
    public void Plugin_registers_every_migrator()
    {
        IEnumerable<string> registered = Regex
            .Matches(RepoFile("BOCCHI", "Config", "ConfigMigrator.cs"), @"new (ConfigMigratorV\d+ToV\d+)\(\)")
            .Select(m => m.Groups[1].Value);

        Assert.Equal(Migrators.Select(m => m.GetType().Name).Order(), registered.Order());
    }

    public static TheoryData<int> StartVersions() => new(Enumerable.Range(1, CurrentVersion - 1));

    [Theory]
    [MemberData(nameof(StartVersions))]
    public void A_config_with_missing_sections_migrates_to_current(int startVersion)
    {
        ConfigurationMigrationResolver resolver = new(Migrators);
        JObject config = new() { ["Version"] = startVersion };

        int version = startVersion;
        while (version < CurrentVersion)
        {
            IMigrator? migrator = resolver.Resolve(version);
            Assert.NotNull(migrator);
            config = migrator.Migrate(config);
            int next = config["Version"]?.Value<int>() ?? 1;
            Assert.True(next > version, $"migrator from {version} did not raise Version");
            version = next;
        }

        Assert.Equal(CurrentVersion, version);
    }
}
