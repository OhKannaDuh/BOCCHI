using BOCCHI.Common.Data.CriticalEncounters;

namespace BOCCHI.Automator.Services.Goals;

public interface IStartableCriticalEncounterFinder
{
    CriticalEncounter? FindStartable();
}
