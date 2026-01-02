using System.Numerics;
using Dalamud.Game.ClientState.Fates;
using Ocelot.Services.Data;
using FateData = Lumina.Excel.Sheets.Fate;

namespace BOCCHI.Common.Data.Fates;

public class Fate(FateId id, IFate context, IDataRepository<FateData> fateDataRepository)
{
    public readonly FateId Id = id;

    public Vector3 Position { get; private set; } = context.Position;

    public FateState State { get; private set; } = context.State;

    public readonly string Name = context.Name.ToString();

    public readonly float Radius = context.Radius;

    public byte Progress { get; private set; } = context.Progress;

    public readonly FateProgressTracker ProgressTracker = new();

    public readonly FateData GameData = fateDataRepository.Get(context.FateId);

    public void Update(IFate context)
    {
        Position = context.Position;
        State = context.State;
        Progress = context.Progress;

        ProgressTracker.Observe(this);
    }
}
