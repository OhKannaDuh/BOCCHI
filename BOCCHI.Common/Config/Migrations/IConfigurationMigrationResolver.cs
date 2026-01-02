using Newtonsoft.Json.Linq;

namespace BOCCHI.Common.Config.Migrations;

public interface IConfigurationMigrationResolver
{
    IMigrator? Resolve(int from);

    IMigrator? Resolve(JObject obj);

    bool CanMigrateTo(int from, int to);
}
