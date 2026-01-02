using BOCCHI.Common.Data.CriticalEncounters;
using FFXIVClientStructs.FFXIV.Client.Game.InstanceContent;

namespace BOCCHI.CriticalEncounters.Data;

public interface ICriticalEncounterFactory
{
    CriticalEncounter Create(DynamicEvent ev);
}
