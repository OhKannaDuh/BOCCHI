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

        int version = config["Version"]?.Value<int>() ?? 1;

        return version < Configuration.CurrentVersion;
    }

    public void Migrate()
    {
        if (GetCurrentConfigJObject() is not { } config)
        {
            logger.Warning("No config file found");
            return;
        }

        ConfigurationMigrationResolver resolver = new([
            new ConfigMigratorV1ToV2(),
            new ConfigMigratorV2ToV3(),
            new ConfigMigratorV3ToV4(),
            new ConfigMigratorV4ToV5(),
            new ConfigMigratorV5ToV6(),
            new ConfigMigratorV6ToV7(),
            new ConfigMigratorV7ToV8(),
            new ConfigMigratorV8ToV9(),
            new ConfigMigratorV9ToV10(),
            new ConfigMigratorV10ToV11(),
            new ConfigMigratorV11ToV12(),
            new ConfigMigratorV12ToV13(),
            new ConfigMigratorV13ToV14(),
            new ConfigMigratorV14ToV15(),
            new ConfigMigratorV15ToV16(),
            new ConfigMigratorV16ToV17(),
            new ConfigMigratorV17ToV18(),
            new ConfigMigratorV18ToV19(),
            new ConfigMigratorV19ToV20(),
            new ConfigMigratorV20ToV21(),
            new ConfigMigratorV21ToV22(),
            new ConfigMigratorV22ToV23(),
            new ConfigMigratorV23ToV24()
        ]);

        int version = config["Version"]?.Value<int>() ?? 1;
        if (!resolver.CanMigrateTo(version, Configuration.CurrentVersion))
        {
            FailMigration(version);
            return;
        }

        JObject? latest = Migrate(config, version, resolver);
        if (latest == null)
        {
            FailMigration(version);
            return;
        }

        Configuration? output = latest.ToObject<Configuration>();
        if (output == null)
        {
            FailMigration(version);
            return;
        }

        logger.Info("Successfully migrated config from {0} to {1}.", version, Configuration.CurrentVersion);
        BackupConfig(version);
        File.WriteAllText(GetConfigFilePath(), JsonConvert.SerializeObject(output, Formatting.Indented));
    }

    private void FailMigration(int version)
    {
        logger.Warning(
            "Could not migrate configuration from {0} to {1}. Backing up to {2}",
            version,
            Configuration.CurrentVersion,
            GetConfigFileBackupPath(version));
        BackupConfig(version);
    }

    private JObject? Migrate(JObject config, int version, ConfigurationMigrationResolver resolver)
    {
        do
        {
            IMigrator? migrator = resolver.Resolve(version);
            if (migrator == null)
            {
                return null;
            }

            config = migrator.Migrate(config);
            version = config["Version"]?.Value<int>() ?? 1;
        }
        while (version < Configuration.CurrentVersion);

        return config;
    }

    private void BackupConfig(int version)
    {
        string path = GetConfigFilePath();
        if (!File.Exists(path))
        {
            logger.Warning("Tried backing up config of version {0} but could not read config from path {1}.", version, path);
            return;
        }

        string backupPath = GetConfigFileBackupPath(version);

        try
        {
            string? dir = Path.GetDirectoryName(backupPath);
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

    public void PruneBackups()
    {
        try
        {
            var backups = Directory
                .EnumerateFiles(GetPluginConfigsDirectory(), $"{BackupPrefix}*.json")
                .Select(path => (Path: path, Version: TryParseBackupVersion(path)))
                .Where(b => b.Version != null)
                .OrderByDescending(b => b.Version);

            foreach ((string path, int? version) in backups.Skip(1))
            {
                File.Delete(path);
                logger.Info("Removed old config backup v{0}: {1}", version, path);
            }
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Failed to prune old config backups");
        }
    }

    private string GetPluginConfigsDirectory() => Directory.GetParent(plugin.GetPluginConfigDirectory())!.FullName;

    private string GetConfigFilePath() =>
        Path.Combine(GetPluginConfigsDirectory(), $"{plugin.InternalName}.json");

    private string BackupPrefix => $"{plugin.InternalName}.";

    private string GetConfigFileBackupPath(int version) =>
        Path.Combine(GetPluginConfigsDirectory(), $"{BackupPrefix}{version}.json");

    private int? TryParseBackupVersion(string path) =>
        int.TryParse(Path.GetFileNameWithoutExtension(path)[BackupPrefix.Length..], out int version) ? version : null;

    private JObject? GetCurrentConfigJObject()
    {
        string filePath = GetConfigFilePath();

        if (!File.Exists(filePath))
        {
            return null;
        }

        string raw = File.ReadAllText(filePath);
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
