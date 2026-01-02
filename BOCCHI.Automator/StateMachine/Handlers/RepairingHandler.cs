using BOCCHI.Automator.Data;
using BOCCHI.Common.Data.Zones;
using BOCCHI.Common.Services;
using Ocelot.Chain;
using Ocelot.States.Score;

namespace BOCCHI.Automator.StateMachine.Handlers;

public class RepairingHandler(IZoneProvider zones, IRepairService repair) : ScoreStateHandler<AutomatorState, StatePriority>(AutomatorState.Repairing)
{
    private Task<ChainResult>? task;

    public override StatePriority GetScore()
    {
        if (task is { IsCompleted: false })
        {
            return StatePriority.Always;
        }

        return zones.GetZone().IsInBasecamp() && repair.ShouldRepair() ? StatePriority.AboveNormal : StatePriority.Never;
    }

    public override void Enter()
    {
        base.Enter();
        task = repair.Repair().ExecuteAsync();
    }


    public override void Exit(AutomatorState next)
    {
        base.Exit(next);
        if (task is { IsCompleted: true })
        {
            task.Dispose();
            task = null;
        }
    }

    public override void Handle()
    {
        // Do nothing this is handled by the task
    }
}
