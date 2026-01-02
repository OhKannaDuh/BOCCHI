using BOCCHI.Common.Data.CriticalEncounters;
using BOCCHI.Common.Services;

namespace BOCCHI.CriticalEncounters.Services;

public class CriticalEncounterContext : ICriticalEncounterContext
{
    public bool IsInCriticalEncounter()
    {
        return false;
    }

    public CriticalEncounterId? GetCriticalEncounterId()
    {
        throw new NotImplementedException();
    }
}
