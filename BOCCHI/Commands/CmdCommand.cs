using BOCCHI.Common.Data.CriticalEncounters;
using BOCCHI.Common.Data.Fates;
using BOCCHI.Common.Data.Zones;
using BOCCHI.Common.Services;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.InstanceContent;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Ocelot.Services.Commands;
using Ocelot.Services.Translation;

namespace BOCCHI.Commands;

public class CmdCommand
(
    ICriticalEncounterRepository criticalEncounters,
    IFateRepository fates,
    IZoneProvider zones,
    IClientState client,
    IChatGui chat,
    ITranslator<CmdCommand> translator
) : OcelotCommand(translator)
{
    public override string Command => "cmd";

    public override List<string> Aliases => [];

    public override unsafe void Execute(CommandContext context)
    {
        if (context.Args.Length == 0)
        {
            chat.PrintError("Usage: /bocchi cmd flag-active-ce | flag-active-fate | flag-active-non-pot-fate");
            return;
        }

        AgentMap* map = AgentMap.Instance();
        if (map == null)
        {
            chat.PrintError("Map agent unavailable.");
            return;
        }

        switch (context.Args[0].ToLowerInvariant())
        {
            case "flag-active-ce":
                FlagActiveCe(map);
                break;
            case "flag-active-fate":
                FlagActiveFate(map, ignorePots: false);
                break;
            case "flag-active-non-pot-fate":
                FlagActiveFate(map, ignorePots: true);
                break;
            default:
                chat.PrintError("Unknown argument. Try flag-active-ce, flag-active-fate, or flag-active-non-pot-fate.");
                break;
        }
    }

    private unsafe void FlagActiveCe(AgentMap* map)
    {
        foreach (CriticalEncounter encounter in criticalEncounters.SnapshotWithoutForkedTower())
        {
            if (encounter.State != DynamicEventState.Register || float.IsNaN(encounter.Position.X))
            {
                continue;
            }

            map->FlagMarkerCount = 0;
            map->SetFlagMapMarker(client.TerritoryType, client.MapId, encounter.Position);
            chat.Print($"Flagged CE: {encounter.Name}");
            return;
        }

        chat.Print("No registering critical encounter found.");
    }

    private unsafe void FlagActiveFate(AgentMap* map, bool ignorePots)
    {
        IZone zone = zones.GetZone();
        foreach (Fate fate in fates.Snapshot())
        {
            if (ignorePots && zone.IsPotFate(fate.Id.Value))
            {
                continue;
            }

            map->FlagMarkerCount = 0;
            map->SetFlagMapMarker(client.TerritoryType, client.MapId, fate.Position);
            chat.Print($"Flagged fate: {fate.Name}");
            return;
        }

        chat.Print(ignorePots ? "No non-pot fate found." : "No fate found.");
    }
}
