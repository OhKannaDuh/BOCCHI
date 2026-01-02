using BOCCHI.Common.Data.CriticalEncounters;
using BOCCHI.Common.Services;
using BOCCHI.CriticalEncounters.Data;
using ECommons;
using FFXIVClientStructs.FFXIV.Client.Game.InstanceContent;
using Ocelot.Lifecycle;
using Ocelot.Services.Data;

namespace BOCCHI.CriticalEncounters.Services;

public class CriticalEncounterRepository(
    IDataRepository<CriticalEncounterId, CriticalEncounter> data,
    ICriticalEncounterFactory factory
) : ICriticalEncounterRepository, IOnUpdate
{
    public event Action<CriticalEncounter>? CriticalEncounterAdded;

    public event Action<CriticalEncounterId>? CriticalEncounterRemoved;

    public IReadOnlyList<CriticalEncounter> Snapshot()
    {
        return data.GetAll().ToList();
    }

    public IReadOnlyList<CriticalEncounter> SnapshotWithoutForkedTower()
    {
        return data.Where(e => e.Id.Value != 48).ToList().AsReadOnly();
    }

    public bool HasCriticalEncounter(CriticalEncounterId id)
    {
        return data.ContainsKey(id);
    }

    public unsafe void Update()
    {
        var oc = PublicContentOccultCrescent.GetInstance();
        if (oc == null)
        {
            var ids = data.GetKeys();
            foreach (var id in ids)
            {
                data.Remove(id);
            }
            return;
        }

        var current = oc->DynamicEventContainer.Events
            .ToArray()
            .Where(e => e.State != DynamicEventState.Inactive)
            .Select(factory.Create)
            .ToDictionary(k => k.Id, v => v);

        foreach (var (id, fate) in current)
        {
            if (data.TryAdd(id, fate))
            {
                CriticalEncounterAdded?.Invoke(fate);
            }
        }

        var despawned = data.GetKeys().Except(current.Keys).ToList();
        foreach (var id in despawned)
        {
            if (data.Remove(id))
            {
                CriticalEncounterRemoved?.Invoke(id);
            }
        }

        foreach (var criticalEncounter in data.GetAll())
        {
            var ev = oc->DynamicEventContainer.Events.ToArray().FirstOrNull(e => e.DynamicEventId ==  criticalEncounter.Id.Value);
            if (ev == null)
            {
                continue;
            }

            criticalEncounter.Update(ev.Value);
        }
    }
}
