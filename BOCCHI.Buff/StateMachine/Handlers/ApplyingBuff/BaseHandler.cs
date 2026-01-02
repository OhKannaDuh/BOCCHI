using BOCCHI.Buff.Data;
using BOCCHI.Buff.Services;
using BOCCHI.Common.Data.SupportJobs;
using BOCCHI.Common.Services;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.InstanceContent;
using Ocelot.Actions;
using Ocelot.Extensions;
using Ocelot.Services.PlayerState;
using Ocelot.States.Flow;

namespace BOCCHI.Buff.StateMachine.Handlers.ApplyingBuff;

public abstract class BaseHandler(
    BuffState state,
    IBuffProvider buffs,
    IObjectTable objects,
    ICondition conditions,
    ISupportJobFactory supportJobs,
    ISupportJobChanger changer
) : FlowStateHandler<BuffState>(state)
{
    private readonly BuffState state = state;

    private DateTime lastCast = DateTime.MinValue;

    public override void Enter()
    {
        base.Enter();
        lastCast = DateTime.MinValue - TimeSpan.FromSeconds(3);
    }

    public override BuffState? Handle()
    {
        var buff = GetBuffData();
        var remaining = GetMinutesRemainingForBuff(buff);
        if (remaining >= 29)
        {
            return BuffState.ChoosingBuffToApply;
        }

        if (conditions[ConditionFlag.Mounted])
        {
            if (Actions.Dismount.CanCast())
            {
                Actions.Dismount.Cast();
            }

            return null;
        }

        if (!IsCorrectJob())
        {
            if (!changer.IsBusy())
            {
                changer.Change(buff.SupportJobId);
            }

            return null;
        }

        var time = DateTime.UtcNow - lastCast;
        if (buff.Action.CanCast() && time.TotalSeconds >= 3)
        {
            lastCast = DateTime.UtcNow;
            buff.Action.Cast();
        }

        return null;
    }

    private BuffData GetBuffData()
    {
        return buffs.GetBuffForState(state);
    }

    private uint GetMinutesRemainingForBuff(BuffData buff)
    {
        if (objects.LocalPlayer is not { } player)
        {
            return 0;
        }

        if (!player.StatusList.TryGet(buff.StatusId, out var status))
        {
            return 0;
        }

        return (uint)TimeSpan.FromSeconds(status.RemainingTime).TotalMinutes;
    }

    private  bool IsCorrectJob()
    {
        return supportJobs.TryGetCurrent(out var supportJob) && supportJob.Id == GetBuffData().SupportJobId;
    }
}
