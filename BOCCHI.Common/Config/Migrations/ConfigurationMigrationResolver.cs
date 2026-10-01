using Newtonsoft.Json.Linq;

namespace BOCCHI.Common.Config.Migrations;

public interface IMigrator
{
    int FromVersion { get; }

    int ToVersion { get; }

    JObject Migrate(JObject oldConfig);
}

public class DuplicateMigrationBaseException(int from) : Exception($"Found duplicate from migrator {from}");

public static class JObjectExtensions
{
    extension(JObject self)
    {
        public bool BoolOr(string path, bool fallback) => self.SelectToken(path)?.Value<bool>() ?? fallback;

        public int IntOr(string path, int fallback) => self.SelectToken(path)?.Value<int>() ?? fallback;
    }

    public static void MoveIfPresent(JObject source, JObject target, string key)
    {
        if (source[key] is JToken value)
        {
            target[key] = value.DeepClone();
        }
    }

    public static void MoveIfPresent(JObject source, JObject target, params string[] keys)
    {
        foreach (string key in keys)
        {
            MoveIfPresent(source, target, key);
        }
    }

    public static JObject EnsureObject(JObject root, string key, string typeName)
    {
        if (root[key] is JObject existing)
        {
            return existing;
        }

        JObject created = new() { ["$type"] = typeName };
        root[key] = created;
        return created;
    }
}

public class ConfigurationMigrationResolver
{
    private readonly Dictionary<int, IMigrator> migratorMap = [];

    public ConfigurationMigrationResolver(IEnumerable<IMigrator> migrators)
    {
        foreach (IMigrator migrator in migrators)
        {
            if (!migratorMap.TryAdd(migrator.FromVersion, migrator))
            {
                throw new DuplicateMigrationBaseException(migrator.FromVersion);
            }
        }
    }

    public IMigrator? Resolve(int from) => migratorMap.TryGetValue(from, out IMigrator? migrator) ? migrator : null;

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

        HashSet<int> visited = new();
        int current = from;

        while (migratorMap.TryGetValue(current, out IMigrator? migrator))
        {
            if (!visited.Add(current))
            {
                return false;
            }

            int next = migrator.ToVersion;

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
