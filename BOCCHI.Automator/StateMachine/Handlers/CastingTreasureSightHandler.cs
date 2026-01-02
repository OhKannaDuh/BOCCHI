using BOCCHI.Automator.Data;
using BOCCHI.Automator.Data.StateMemory;
using BOCCHI.Common.Data.StateMemory;
using BOCCHI.Common.Data.SupportJobs;
using BOCCHI.Common.Data.Zones;
using BOCCHI.Common.Services;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using Ocelot.Actions;
using Ocelot.Services.Logger;
using Ocelot.Services.PlayerState;
using Ocelot.States.Score;

namespace BOCCHI.Automator.StateMachine.Handlers;

public class CastingTreasureSightHandler(
    IObjectTable objects,
    ICondition conditions,
    IZoneProvider zone,
    ISupportJobFactory supportJobs,
    ISupportJobChanger changer,
    IAutomatorMemory memory,
    ILogger<CastingTreasureSightHandler> logger
) : ScoreStateHandler<AutomatorState, StatePriority>(AutomatorState.CastingTreasureSight)
{
    private DateTime lastCast = DateTime.MinValue;

    private readonly TimeSpan recastInterval = TimeSpan.FromMinutes(2);

    public override StatePriority GetScore()
    {
        if (memory.TryRemember<CastingTreasureSightMemory>(out var _))
        {
            return StatePriority.MediumHigh;
        }

        var freelancer = supportJobs.Create(SupportJobId.PhantomFreelancer);
        if (freelancer.Level < 10)
        {
            return StatePriority.Never;
        }

        if (zone.GetZone().IsInBasecamp() /** && config.ShouldCastTreasureSight */ && GetLastCastDelta() >= recastInterval)
        {
            return StatePriority.Always;
        }

        return StatePriority.Never;
    }

    public override void Enter()
    {
        base.Enter();

        if (supportJobs.TryGetCurrent(out var current))
        {
            memory.TryAdd(new SupportJobMemory(current.Id));
        }

        memory.TryAdd<CastingTreasureSightMemory>();
    }

    public override void Handle()
    {
        if (!supportJobs.TryGetCurrent(out var current))
        {
            return;
        }

        if (conditions[ConditionFlag.Mounted])
        {
            if (Actions.Dismount.CanCast())
            {
                Actions.Dismount.Cast();
            }

            return;
        }

        if (current.Id != SupportJobId.PhantomFreelancer)
        {
            if (!changer.IsBusy())
            {
                changer.Change(SupportJobId.PhantomFreelancer);
            }

            return;
        }

        if (Actions.PhantomActionII.CanCast())
        {
            if (Actions.PhantomActionII.Cast())
            {
                lastCast = DateTime.Now;
                memory.Forget<CastingTreasureSightMemory>();
            }
        }
    }

    private TimeSpan GetLastCastDelta()
    {
        return DateTime.Now - lastCast;
    }
}
