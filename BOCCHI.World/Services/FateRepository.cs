using BOCCHI.Common.Data.Fates;
using BOCCHI.Common.Data.Zones;
using BOCCHI.Common.Services;
using BOCCHI.Common.Services.Data;
using BOCCHI.Fates.Data;
using Dalamud.Game.ClientState.Fates;
using Dalamud.Plugin.Services;
using Ocelot.Lifecycle;
using Ocelot.Services.Data;
using System.Numerics;
using FateState = Dalamud.Game.ClientState.Fates.FateState;

namespace BOCCHI.Fates.Services;

public class FateRepository
(
    IDataRepository<FateId, Fate> data,
    IFateTable fates,
    IFateFactory factory,
    IZoneProvider zones
) : IFateRepository, IOnUpdate
{
    public UpdateLimit UpdateLimit =>
        new()
        {
            Mode = UpdateLimitMode.Milliseconds,
            Limit = 500
        };

    public event Action<Fate>? FateAdded;

    public event Action<FateId>? FateRemoved;

    private IReadOnlyList<Fate> snapshot = [];

    public IReadOnlyList<Fate> Snapshot()
    {
        if (snapshot.Count == 0 && data.GetAll().Any())
        {
            snapshot = data.GetAll().ToList();
        }

        return snapshot;
    }

    public bool HasFate(FateId id) => data.ContainsKey(id);

    public void Update()
    {
        if (!zones.GetZone().IsOccultCrescentZone())
        {
            if (snapshot.Count > 0)
            {
                RepositorySync.ApplySnapshot(data, new Dictionary<FateId, Fate>(), FateAdded, FateRemoved);
                snapshot = [];
            }

            return;
        }

        Dictionary<ushort, IFate> live = [];
        Dictionary<FateId, Fate> current = [];
        foreach (IFate fate in fates)
        {
            live[fate.FateId] = fate;

            if (fate.State is not (FateState.Preparing or FateState.Running)
                || fate.Position == Vector3.Zero
                || fate.Position == Vector3.NaN)
            {
                continue;
            }

            Fate created = factory.Create(fate);
            current[created.Id] = created;
        }

        RepositorySync.ApplySnapshot(data, current, FateAdded, FateRemoved);

        List<Fate> tracked = data.GetAll().ToList();
        foreach(Fate fate in tracked)
        {
            if (live.TryGetValue(fate.Id.Value, out IFate? context))
            {
                fate.Update(context);
            }
        }

        snapshot = tracked;
    }
}
