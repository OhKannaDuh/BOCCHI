using BOCCHI.Common.Config.Migrations;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BOCCHI.Config;

public class ConfigMigrator(IDalamudPluginInterface plugin, IPluginLog logger)
{
    public bool ShouldMigrate()
    {
        if (GetCurrentConfigJObject() is not { } config)
        {
            return false;
        }

        var version = config["Version"]?.Value<int>() ?? 1;

        return version < Configuration.CurrentVersion;
    }

    public void Migrate()
    {
        // We shouldn't be able to get into this block, but just in case
        if (GetCurrentConfigJObject() is not { } config)
        {
            logger.Warning("No config file found");
            return;
        }

        var resolver = new ConfigurationMigrationResolver([
            new ConfigMigratorV1ToV2(),
        ]);

        var version = config["Version"]?.Value<int>() ?? 1;
        if (!resolver.CanMigrateTo(version, Configuration.CurrentVersion))
        {
            logger.Warning("Could not migrate configuration from {0} to {1}. Backing up to {2}", version, Configuration.CurrentVersion, GetConfigFileBackupPath(version));
            BackupConfig(version);
            return;
        }

        var latest = Migrate(config, version, resolver);
        if (latest == null)
        {
            logger.Warning("Could not migrate configuration from {0} to {1}. Backing up to {2}", version, Configuration.CurrentVersion, GetConfigFileBackupPath(version));
            BackupConfig(version);
            return;
        }

        var output = latest.ToObject<Configuration>();
        if (output == null)
        {
            logger.Warning("Could not migrate configuration from {0} to {1}. Backing up to {2}", version, Configuration.CurrentVersion, GetConfigFileBackupPath(version));
            BackupConfig(version);
            return;
        }

        logger.Info("Successfully migrated config from {0} to {1}.", version, Configuration.CurrentVersion);
        BackupConfig(version);
        var serialized = JsonConvert.SerializeObject(
            output,
            Formatting.Indented
        );

        var path = GetConfigFilePath();
        File.WriteAllText(path, serialized);
    }

    private JObject? Migrate(JObject config, int version, ConfigurationMigrationResolver resolver)
    {
        do
        {
            var migrator = resolver.Resolve(version);
            if (migrator == null)
            {
                return null;
            }

            config = migrator.Migrate(config);
            version = config["Version"]?.Value<int>() ?? 1;
        } while (version < Configuration.CurrentVersion);

        return config;
    }

     private void BackupConfig(int version)
    {
        var path = GetConfigFilePath();
        if (!File.Exists(path))
        {
            logger.Warning("Tried backing up config of version {0} but could to read config from path {1}.", version, path);
            return;
        }

        var backupPath = GetConfigFileBackupPath(version);

        try
        {
            var dir = Path.GetDirectoryName(backupPath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            if (File.Exists(backupPath))
            {
                logger.Warning("Config backup already exists: {Path}", backupPath);
                return;
            }

            File.Copy(path, backupPath);
            logger.Info("Backed up config file to {Path}", backupPath);
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Failed to back up config file");
        }
    }

    private string GetConfigFilePath()
    {
        var fileName = $"{plugin.InternalName}.json";
        var pluginDir = plugin.GetPluginConfigDirectory();
        var pluginConfigsDir = Directory.GetParent(pluginDir)!.FullName;

        return Path.Combine(pluginConfigsDir, fileName);
    }

    private string GetConfigFileBackupPath(int version)
    {
        var fileName = $"{plugin.InternalName}.{version}.json";
        var pluginDir = plugin.GetPluginConfigDirectory();
        var pluginConfigsDir = Directory.GetParent(pluginDir)!.FullName;

        return Path.Combine(pluginConfigsDir, fileName);
    }

    private JObject? GetCurrentConfigJObject()
    {
        var filePath = GetConfigFilePath();

        if (!File.Exists(filePath))
        {
            return null;
        }

        var raw = File.ReadAllText(filePath);
        try
        {
            return JObject.Parse(raw);
        }
        catch (JsonReaderException e)
        {
            logger.Error(e, "An error occured when trying to parse the config file: {0}", filePath);
            return null;
        }
    }
}
