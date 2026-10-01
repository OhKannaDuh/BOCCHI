using System.Collections.Concurrent;
using BOCCHI.Common.Config;
using Dalamud.Interface;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using ECommons.Reflection;
using Ocelot.Services.Logger;
using Ocelot.Services.Translation;

namespace BOCCHI.Common.Services;

public sealed record DependencyPlugin(string DisplayName, string InternalName, params string[] RepositoryUrls)
{
    public bool CanInstall => RepositoryUrls.Length > 0;

    public string PrimaryRepositoryUrl => RepositoryUrls[0];
}

public static class DependencyPlugins
{
    private const string Veyn = "https://puni.sh/api/repository/veyn";

    private const string Punish = "https://love.puni.sh/ment.json";

    public static readonly DependencyPlugin VNavmesh = new("vnavmesh", "vnavmesh", Veyn);

    public static readonly DependencyPlugin Lifestream = new(
        "Lifestream",
        "Lifestream",
        "https://github.com/NightmareXIV/MyDalamudPlugins/raw/main/pluginmaster.json",
        "https://raw.githubusercontent.com/NightmareXIV/MyDalamudPlugins/main/pluginmaster.json");

    public static readonly DependencyPlugin Knightshopper = new("Knightshopper", "Knightshopper", "https://puni.sh/api/repository/knightmore");

    public static readonly DependencyPlugin WrathCombo = new("Wrath Combo", "WrathCombo", Punish, "https://puni.sh/api/plugins");

    public static readonly DependencyPlugin RotationSolverReborn = new("Rotation Solver Reborn", CombatPluginPresence.RotationSolver);

    public static readonly DependencyPlugin BossMod = new("BossMod", "BossMod", Veyn);

    public static readonly DependencyPlugin BossModReborn = new("BossMod Reborn", "BossModReborn");
}

public sealed class DependencyPluginInstaller(
    IDalamudPluginInterface plugin,
    IChatGui chat,
    UIConfig ui,
    ILogger<DependencyPluginInstaller> logger
)
{
    private const string StatusKey = "config.dependencies.fields.status";

    private readonly ConcurrentDictionary<string, byte> installing = new();

    public bool IsInstalling(DependencyPlugin dependency) => installing.ContainsKey(dependency.InternalName);

    public bool IsRepositoryAdded(DependencyPlugin dependency)
    {
        if (!dependency.CanInstall)
        {
            return false;
        }

        try
        {
            return dependency.RepositoryUrls.Any(DalamudReflector.HasRepo);
        }
        catch (Exception e)
        {
            logger.Debug("[PluginInstall] Could not read custom repositories: {Message}", e.Message);
            return false;
        }
    }

    public void OpenInstaller(DependencyPlugin dependency) =>
        plugin.OpenPluginInstallerTo(PluginInstallerOpenKind.InstalledPlugins, dependency.DisplayName);

    public void AddRepository(DependencyPlugin dependency, ITranslator translator)
    {
        if (!dependency.CanInstall)
        {
            return;
        }

        if (IsRepositoryAdded(dependency))
        {
            Print(translator, "repo_exists", dependency);
            return;
        }

        try
        {
            DalamudReflector.AddRepo(dependency.PrimaryRepositoryUrl, true);
            DalamudReflector.SaveDalamudConfig();
            DalamudReflector.ReloadPluginMasters();
            Print(translator, "repo_added", dependency);
        }
        catch (Exception e)
        {
            logger.Error(e, "[PluginInstall] Failed to add repository {Url}", dependency.PrimaryRepositoryUrl);
            PrintError(translator, "repo_failed", dependency);
        }
    }

    public void Install(DependencyPlugin dependency, ITranslator translator)
    {
        if (!dependency.CanInstall || !installing.TryAdd(dependency.InternalName, 0))
        {
            return;
        }

        string repoUrl = dependency.RepositoryUrls.FirstOrDefault(SafeHasRepo) ?? dependency.PrimaryRepositoryUrl;
        _ = InstallAsync(dependency, repoUrl, translator);
    }

    private async Task InstallAsync(DependencyPlugin dependency, string repoUrl, ITranslator translator)
    {
        try
        {
            if (await DalamudReflector.AddPlugin(repoUrl, dependency.InternalName))
            {
                Print(translator, "installed", dependency);
            }
            else
            {
                PrintError(translator, "install_failed", dependency);
            }
        }
        catch (Exception e)
        {
            logger.Error(e, "[PluginInstall] Failed to install {Plugin} from {Url}", dependency.InternalName, repoUrl);
            PrintError(translator, "install_failed", dependency);
        }
        finally
        {
            installing.TryRemove(dependency.InternalName, out _);
        }
    }

    private bool SafeHasRepo(string url)
    {
        try
        {
            return DalamudReflector.HasRepo(url);
        }
        catch
        {
            return false;
        }
    }

    private void Print(ITranslator translator, string field, DependencyPlugin dependency) =>
        BocchiChat.Print(chat, ui, translator.T($"{StatusKey}.{field}", ("plugin", dependency.DisplayName)));

    private void PrintError(ITranslator translator, string field, DependencyPlugin dependency) =>
        BocchiChat.PrintError(chat, ui, translator.T($"{StatusKey}.{field}", ("plugin", dependency.DisplayName)));
}
