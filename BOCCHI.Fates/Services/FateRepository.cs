using System.Numerics;
using BOCCHI.Common.Data.Fates;
using BOCCHI.Common.Services;
using BOCCHI.Fates.Data;
using Dalamud.Game.ClientState.Fates;
using Dalamud.Plugin.Services;
using Ocelot.Lifecycle;
using Ocelot.Services.Data;

namespace BOCCHI.Fates.Services;

public class FateRepository(
    IDataRepository<FateId, Fate> data,
    IFateTable fates,
    IFateFactory factory
) : IFateRepository, IOnUpdate
{
    public event Action<Fate>? FateAdded;

    public event Action<FateId>? FateRemoved;

    public IReadOnlyList<Fate> Snapshot()
    {
        return data.GetAll().ToList();
    }

    public bool HasFate(FateId id)
    {
        return data.ContainsKey(id);
    }

    public void Update()
    {
        var current = fates
            .Where(f => f.State is FateState.Preparation or FateState.Running)
            .Where(f => f.Position != Vector3.Zero && f.Position != Vector3.NaN)
            .Select(factory.Create)
            .ToDictionary(f => f.Id, f => f);

        foreach (var (id, fate) in current)
        {
            if (data.TryAdd(id, fate))
            {
                FateAdded?.Invoke(fate);
            }
        }

        var despawned = data.GetKeys().Except(current.Keys).ToList();
        foreach (var id in despawned)
        {
            if (data.Remove(id))
            {
                FateRemoved?.Invoke(id);
            }
        }

        foreach (var fate in data.GetAll())
        {
            var context = fates.FirstOrDefault(f => f.FateId == fate.Id.Value);
            if (context == null)
            {
                continue;
            }

            fate.Update(context);
        }
    }
}
