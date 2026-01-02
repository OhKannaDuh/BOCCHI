using Newtonsoft.Json.Linq;

namespace BOCCHI.Common.Config.Migrations;

public class ConfigurationMigrationResolver : IConfigurationMigrationResolver
{
    private Dictionary<int, IMigrator> migratorMap { get; } = [];

    public ConfigurationMigrationResolver(IEnumerable<IMigrator> migrators)
    {
        foreach (var migrator in migrators)
        {
            if (!migratorMap.TryAdd(migrator.FromVersion, migrator))
            {
                throw new DuplicateMigrationBaseException(migrator.FromVersion);
            }
        }
    }

    public IMigrator? Resolve(int from)
    {
        return migratorMap.TryGetValue(from, out var migrator) ? migrator : null;
    }

    public IMigrator? Resolve(JObject obj)
    {
        return Resolve(obj["Version"]?.Value<int>() ?? 1);
    }

    public bool CanMigrateTo(int from, int to)
    {
        if (from == to)
        {
            return true;
        }

        if (to < from)
        {
            return false;
        }

        var visited = new HashSet<int>();
        var current = from;

        while (migratorMap.TryGetValue(current, out var migrator))
        {
            if (!visited.Add(current))
            {
                return false;
            }

            var next = migrator.ToVersion;

            if (next == to)
            {
                return true;
            }

            if (next == current)
            {
                return false;
            }

            current = next;
        }

        return false;
    }
}
