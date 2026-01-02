using BOCCHI.Common.Data.CriticalEncounters;

namespace BOCCHI.Common.Services;

public interface ICriticalEncounterRepository
{
    event Action<CriticalEncounter> CriticalEncounterAdded;

    event Action<CriticalEncounterId> CriticalEncounterRemoved;

    IReadOnlyList<CriticalEncounter> SnapshotWithoutForkedTower();

    IReadOnlyList<CriticalEncounter> Snapshot();

    bool HasCriticalEncounter(CriticalEncounterId id);
}
