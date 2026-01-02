using BOCCHI.Common.Data.CriticalEncounters;
using FFXIVClientStructs.FFXIV.Client.Game.InstanceContent;
using Ocelot.Services.Logger;

namespace BOCCHI.CriticalEncounters.Data;

public class CriticalEncounterFactory(ILogger log) : ICriticalEncounterFactory
{
    public CriticalEncounter Create(DynamicEvent ev)
    {
        var id = new CriticalEncounterId(ev.DynamicEventId);

        return new CriticalEncounter(id, ev,log);
    }
}
