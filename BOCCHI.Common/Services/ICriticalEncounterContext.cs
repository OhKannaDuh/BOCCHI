using BOCCHI.Common.Data.CriticalEncounters;
using BOCCHI.Common.Data.Fates;

namespace BOCCHI.Common.Services;

public interface ICriticalEncounterContext
{
    bool IsInCriticalEncounter();

    CriticalEncounterId? GetCriticalEncounterId();
}
