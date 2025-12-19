using System.Linq;
using BOCCHI.Data;
using BOCCHI.Enums;
using BOCCHI.Modules.Teleporter;
using Dalamud.Bindings.ImGui;
using Ocelot.Ui;

namespace BOCCHI.Modules.Debug.Panels;

public class TeleporterPanel : Panel
{
    public override string GetName()
    {
        return "Teleporter";
    }

    public override void Render(DebugModule module)
    {
        if (module.TryGetModule<TeleporterModule>(out var teleporter) && teleporter!.IsReady())
        {
            OcelotUi.Title("Teleporter:");
            OcelotUi.Indent(() =>
            {
                var shards = ZoneData.GetNearbyAethernetShards();
                if (shards.Count > 0)
                {
                    OcelotUi.Title("Nearby Aethernet Shards:");
                    OcelotUi.Indent(() =>
                    {
                        foreach (var shard in ZoneData.GetNearbyAethernetShards())
                        {
                            var data = AethernetData.All().First(o => o.BaseId == shard.BaseId);
                            ImGui.TextUnformatted(data.Aethernet.ToFriendlyString());
                        }
                    });
                }

                if (ImGui.Button("Test Return"))
                {
                    teleporter.teleporter.Return();
                }
            });
        }
    }
}
