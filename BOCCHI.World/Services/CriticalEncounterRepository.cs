using BOCCHI.Common.Data.CriticalEncounters;
using BOCCHI.Common.Data.Zones;
using BOCCHI.Common.Data.Zones.Graph;
using BOCCHI.Common.Services;
using BOCCHI.Common.Services.Data;
using BOCCHI.CriticalEncounters.Data;
using ECommons;
using FFXIVClientStructs.FFXIV.Client.Game.InstanceContent;
using Ocelot.Lifecycle;
using Ocelot.Services.Data;

namespace BOCCHI.CriticalEncounters.Services;

public class CriticalEncounterRepository
(
    IDataRepository<CriticalEncounterId, CriticalEncounter> data,
    ICriticalEncounterFactory factory,
    IZoneProvider zones,
    CriticalEncounterGeometry geometry
) : ICriticalEncounterRepository, IOnUpdate
{
    public UpdateLimit UpdateLimit =>
        new()
        {
            Mode = UpdateLimitMode.Milliseconds,
            Limit = 500
        };

    public event Action<CriticalEncounter>? CriticalEncounterAdded;

    public event Action<CriticalEncounterId>? CriticalEncounterRemoved;

    private IReadOnlyList<CriticalEncounter> snapshot = [];

    private IReadOnlyList<CriticalEncounter> snapshotWithoutForkedTower = [];

    public IReadOnlyList<CriticalEncounter> Snapshot()
    {
        EnsureSnapshots();
        return snapshot;
    }

    public IReadOnlyList<CriticalEncounter> SnapshotWithoutForkedTower()
    {
        EnsureSnapshots();
        return snapshotWithoutForkedTower;
    }

    private void EnsureSnapshots()
    {
        if (snapshot.Count > 0 || !data.GetAll().Any())
        {
            return;
        }

        BuildSnapshots(data.GetAll().ToList());
    }

    private void BuildSnapshots(List<CriticalEncounter> tracked)
    {
        snapshot = tracked;

        ushort forkedTowerId = zones.GetZone().ForkedTowerEventId;
        snapshotWithoutForkedTower = forkedTowerId == 0
            ? tracked
            : tracked.Where(e => e.Id.Value != forkedTowerId).ToList();
    }

    public CriticalEncounter? TryGetForkedTower()
    {
        ushort forkedTowerId = zones.GetZone().ForkedTowerEventId;
        if (forkedTowerId == 0)
        {
            return null;
        }

        return data.GetAll().FirstOrDefault(e => e.Id.Value == forkedTowerId);
    }

    public bool HasCriticalEncounter(CriticalEncounterId id) => data.ContainsKey(id);

    public unsafe void Update()
    {
        PublicContentOccultCrescent* oc = PublicContentOccultCrescent.GetInstance();
        if (oc == null)
        {
            foreach(CriticalEncounterId id in data.GetKeys().ToList())
            {
                data.Remove(id);
            }

            snapshot = [];
            snapshotWithoutForkedTower = [];
            return;
        }

        DynamicEvent[] events = oc->DynamicEventContainer.Events.ToArray();
        Dictionary<uint, DynamicEvent> live = [];
        Dictionary<CriticalEncounterId, CriticalEncounter> current = [];
        foreach (DynamicEvent ev in events)
        {
            live[ev.DynamicEventId] = ev;

            if (ev.State == DynamicEventState.Inactive)
            {
                continue;
            }

            CriticalEncounter created = factory.Create(ev);
            current[created.Id] = created;
        }

        RepositorySync.ApplySnapshot(data, current, CriticalEncounterAdded, CriticalEncounterRemoved);

        List<CriticalEncounter> tracked = data.GetAll().ToList();
        IZone zone = zones.GetZone();
        foreach (CriticalEncounter criticalEncounter in tracked)
        {
            if (live.TryGetValue(criticalEncounter.Id.Value, out DynamicEvent ev))
            {
                criticalEncounter.Update(ev);
            }

            if (geometry.TryResolveForAuthored(
                    criticalEncounter.Id.Value,
                    criticalEncounter.Position,
                    out _) is not { Radius: > 0 } area)
            {
                continue;
            }

            ActivityData? authored = zone.GetCriticalEncounterData()
                .FirstOrDefault(a => a.Id == criticalEncounter.Id.Value);
            ActivityAreaShape shape = NavigationConstants.ResolveCriticalEncounterShape(
                zone,
                criticalEncounter.Id.Value,
                area.IsSquare);
            criticalEncounter.ApplyCombatGeometry(area.Radius, shape, area.Center, authored?.CombatRadius);
            zone.ApplyCriticalEncounterCombat(
                criticalEncounter.Id.Value,
                criticalEncounter.UnpaddedCombatRadius,
                shape);
        }

        BuildSnapshots(tracked);
    }
}
