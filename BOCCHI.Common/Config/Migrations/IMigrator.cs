using Newtonsoft.Json.Linq;

namespace BOCCHI.Common.Config.Migrations;

public interface IMigrator
{
    int FromVersion { get; }

    int ToVersion { get; }

    JObject Migrate(JObject oldConfig);
}
