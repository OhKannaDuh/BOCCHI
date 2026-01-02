using System.Numerics;
using ECommons;
using FFXIVClientStructs.FFXIV.Client.Game.InstanceContent;
using FFXIVClientStructs.FFXIV.Client.LayoutEngine;
using Ocelot.Services.Logger;

namespace BOCCHI.Common.Data.CriticalEncounters;

public class CriticalEncounter(CriticalEncounterId id, DynamicEvent ev, ILogger log)
{
    public readonly CriticalEncounterId Id = id;

    public readonly Vector3 Position = GetPosition(ev, log);

    public DynamicEventState State { get; private set; } = ev.State;

    public readonly String Name = ev.Name.ToString();

    public byte Progress { get; private set; } = ev.Progress;

    public readonly CriticalEncounterProgressTracker ProgressTracker = new();

    public float Radius
    {
        get => Id switch
        {
            _ => 20f,
        };
    }

    private static unsafe Vector3 GetPosition(DynamicEvent ev,  ILogger log)
    {
        var layout = LayoutWorld.Instance()->ActiveLayout;
        if (layout == null)
        {
            return Vector3.NaN;
        }

        if (!layout->InstancesByType.TryGetValue(InstanceType.EventObject, out var eventObjects, false))
        {
            return Vector3.NaN;
        }

        var eventObjectId = ev.LGBEventObject;
        if (eventObjectId <= 0)
        {
            return Vector3.NaN;
        }

        var eventObject = eventObjects.Value->Values.FirstOrNull(e => e.Value->Id.InstanceKey == eventObjectId);
        if (eventObject == null)
        {
            return Vector3.NaN;
        }

        var trans = eventObject.Value.Value->GetTransformImpl();
        var position = trans->Translation;
        return new Vector3(position.X, position.Y, position.Z);
    }


    public void Update(DynamicEvent ev)
    {
        State = ev.State;
        Progress = ev.Progress;

        ProgressTracker.Observe(this);
    }
}
