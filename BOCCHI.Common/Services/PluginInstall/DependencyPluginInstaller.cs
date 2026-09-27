using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using BOCCHI.Common.Config;
using Dalamud.Interface;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using ECommons.Reflection;
using Ocelot.Services.Logger;
using Ocelot.Services.Translation;

namespace BOCCHI.Common.Services.PluginInstall;

/// <summary>
///     Adds custom plugin repositories and installs dependency plugins through Dalamud's
///     internal PluginManager (reflection, same approach as Questionable's Dependencies tab).
/// </summary>
public sealed class DependencyPluginInstaller(
    IDalamudPluginInterface plugin,
    IChatGui chat,
    UIConfig ui,
    ILogger<DependencyPluginInstaller> logger
)
{
    private const string StatusKey = "config.dependencies.fields.status";

    private readonly ConcurrentDictionary<string, byte> installing = new(StringComparer.OrdinalIgnoreCase);

    public bool IsInstalling(DependencyPlugin dependency) => installing.ContainsKey(dependency.InstallName);

    public bool IsRepositoryAdded(DependencyPlugin dependency)
    {
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

    public void Install(DependencyPlugin dependency, ITranslator translator) =>
        _ = InstallAsync(dependency, translator);

    private async Task InstallAsync(DependencyPlugin dependency, ITranslator translator)
    {
        if (IsPresent(dependency.InstallName))
        {
            Print(translator, "already_installed", dependency);
            if (!plugin.InstalledPlugins.Any(p => p.IsLoaded && Matches(p.InternalName, dependency.InstallName)))
            {
                OpenInstaller(dependency);
            }

            return;
        }

        if (!installing.TryAdd(dependency.InstallName, 0))
        {
            return;
        }

        var repoUrl = dependency.RepositoryUrls.FirstOrDefault(SafeHasRepo) ?? dependency.PrimaryRepositoryUrl;
        try
        {
            if (await InstallFromRepositoryAsync(repoUrl, dependency.InstallName).ConfigureAwait(false))
            {
                Print(translator, "installed", dependency);
            }
            else if (IsPresent(dependency.InstallName))
            {
                Print(translator, "already_installed", dependency);
            }
            else
            {
                PrintError(translator, "install_failed", dependency);
            }
        }
        catch (Exception e) when (IsInstallConflict(e))
        {
            if (IsPresent(dependency.InstallName))
            {
                Print(translator, "already_installed", dependency);
                return;
            }

            if (TryRemoveOrphanedPluginFiles(dependency.InstallName, out var leftoverPath)
                && await InstallFromRepositoryAsync(repoUrl, dependency.InstallName).ConfigureAwait(false))
            {
                Print(translator, "leftover_removed", dependency);
                return;
            }

            logger.Warn(e, "[PluginInstall] {Plugin} leftover files at {Path} blocked install", dependency.InstallName, leftoverPath ?? "?");
            PrintError(
                translator.T(
                    $"{StatusKey}.leftover_in_use",
                    ("plugin", dependency.DisplayName),
                    ("path", leftoverPath ?? dependency.InstallName)));
        }
        catch (Exception e)
        {
            if (IsPresent(dependency.InstallName))
            {
                Print(translator, "already_installed", dependency);
                return;
            }

            logger.Error(e, "[PluginInstall] Failed to install {Plugin}", dependency.InstallName);
            PrintError(translator, "install_failed", dependency);
        }
        finally
        {
            installing.TryRemove(dependency.InstallName, out _);
        }
    }

    /// <summary>
    ///     ECommons <c>AddPlugin</c> hard-codes <c>InstallPluginAsync</c> arguments, which breaks when
    ///     Dalamud adds optional parameters — bind the live method's parameters by name instead.
    /// </summary>
    private async Task<bool> InstallFromRepositoryAsync(string repoUrl, string internalName)
    {
        var manifests = await DalamudReflector.GetPluginMaster(repoUrl).ConfigureAwait(false);
        if (manifests == null || manifests.Count == 0)
        {
            logger.Error("[PluginInstall] No plugin manifests returned from {Url}", repoUrl);
            return false;
        }

        var manifest = manifests.FirstOrDefault(candidate =>
            string.Equals(candidate.GetFoP("InternalName") as string, internalName, StringComparison.Ordinal));
        if (manifest == null)
        {
            logger.Error("[PluginInstall] {Plugin} was not found in {Url}", internalName, repoUrl);
            return false;
        }

        if (!DalamudReflector.HasRepo(repoUrl))
        {
            DalamudReflector.AddRepo(repoUrl, true);
        }

        DalamudReflector.SaveDalamudConfig();
        DalamudReflector.ReloadPluginMasters();

        if (!IsPresent(internalName))
        {
            TryRemoveOrphanedPluginFiles(internalName, out _);
        }

        var pluginManager = DalamudReflector.GetPluginManager();
        var installMethod = pluginManager.GetType()
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(method => method.Name == "InstallPluginAsync")
            .OrderBy(method => method.GetParameters().Length)
            .FirstOrDefault();
        if (installMethod == null)
        {
            logger.Error("[PluginInstall] PluginManager.InstallPluginAsync was not found");
            return false;
        }

        if (installMethod.Invoke(pluginManager, BindInstallArguments(installMethod, manifest)) is not Task installTask)
        {
            logger.Error("[PluginInstall] InstallPluginAsync did not return a Task");
            return false;
        }

        await installTask.ConfigureAwait(false);

        var localPlugin = installTask.GetFoP("Result");
        return localPlugin?.GetFoP("IsLoaded") is true;
    }

    private static object?[] BindInstallArguments(MethodInfo installMethod, object manifest)
    {
        var parameters = installMethod.GetParameters();
        var arguments = new object?[parameters.Length];
        for (var i = 0; i < parameters.Length; i++)
        {
            arguments[i] = parameters[i].Name switch
            {
                "repoManifest" => manifest,
                "useTesting" => false,
                "reason" => PluginLoadReason.Installer,
                _ => parameters[i].HasDefaultValue
                    ? parameters[i].DefaultValue
                    : parameters[i].ParameterType.IsValueType
                        ? Activator.CreateInstance(parameters[i].ParameterType)
                        : null,
            };
        }

        return arguments;
    }

    private static bool IsInstallConflict(Exception exception)
    {
        for (var current = exception; current != null; current = current.InnerException)
        {
            if (current is IOException or UnauthorizedAccessException)
            {
                return true;
            }
        }

        return false;
    }

    private bool SafeHasRepo(string url)
    {
        try
        {
            return DalamudReflector.HasRepo(url);
        }
        catch (Exception e)
        {
            logger.Debug("[PluginInstall] Could not check repository {Url}: {Message}", url, e.Message);
            return false;
        }
    }

    /// <summary>Also asks PluginManager directly — it can know about a plugin the public list doesn't show yet.</summary>
    private bool IsPresent(string internalName)
    {
        if (plugin.InstalledPlugins.Any(p => Matches(p.InternalName, internalName)))
        {
            return true;
        }

        try
        {
            var pluginManager = DalamudReflector.GetPluginManager();
            if (pluginManager.GetType().GetProperty("InstalledPlugins")?.GetValue(pluginManager) is not IEnumerable installed)
            {
                return false;
            }

            foreach (var entry in installed)
            {
                if (Matches(entry.GetType().GetProperty("InternalName")?.GetValue(entry) as string, internalName))
                {
                    return true;
                }
            }
        }
        catch (Exception e)
        {
            logger.Debug("[PluginInstall] Could not query PluginManager.InstalledPlugins for {Plugin}: {Message}", internalName, e.Message);
        }

        return false;
    }

    /// <summary>A half-removed plugin folder makes Dalamud's install throw; clear it when nothing owns it.</summary>
    private bool TryRemoveOrphanedPluginFiles(string internalName, out string? pluginDir)
    {
        pluginDir = GetInstalledPluginDirectory(internalName);
        if (pluginDir == null || !Directory.Exists(pluginDir) || IsPresent(internalName))
        {
            return true;
        }

        try
        {
            var directory = new DirectoryInfo(pluginDir);
            directory.Attributes &= ~FileAttributes.ReadOnly;
            foreach (var entry in directory.EnumerateFileSystemInfos("*", SearchOption.AllDirectories))
            {
                entry.Attributes &= ~FileAttributes.ReadOnly;
            }

            Directory.Delete(pluginDir, true);
            logger.Info("[PluginInstall] Removed leftover {Plugin} files at {Path}", internalName, pluginDir);
            return !Directory.Exists(pluginDir);
        }
        catch (Exception e)
        {
            logger.Warn(e, "[PluginInstall] Could not remove leftover {Plugin} files at {Path}", internalName, pluginDir);
            return false;
        }
    }

    private string? GetInstalledPluginDirectory(string internalName)
    {
        try
        {
            var directory = DalamudReflector.GetPluginManager().GetFoP("pluginDirectory");
            var root = directory switch
            {
                DirectoryInfo info => info.FullName,
                string path => path,
                _ => directory?.GetFoP("FullName") as string,
            };

            return string.IsNullOrEmpty(root) ? null : Path.Combine(root, internalName);
        }
        catch (Exception e)
        {
            logger.Debug("[PluginInstall] Could not resolve plugin directory for {Plugin}: {Message}", internalName, e.Message);
            return null;
        }
    }

    private static bool Matches(string? a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private void Print(ITranslator translator, string field, DependencyPlugin dependency) =>
        BocchiChat.Print(chat, ui, translator.T($"{StatusKey}.{field}", ("plugin", dependency.DisplayName)));

    private void PrintError(ITranslator translator, string field, DependencyPlugin dependency) =>
        PrintError(translator.T($"{StatusKey}.{field}", ("plugin", dependency.DisplayName)));

    private void PrintError(string message) => BocchiChat.PrintError(chat, ui, message);
}
