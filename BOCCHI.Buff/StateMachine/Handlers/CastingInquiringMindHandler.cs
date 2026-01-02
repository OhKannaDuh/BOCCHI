using BOCCHI.Buff.Data;
using BOCCHI.Common.Data.SupportJobs;
using BOCCHI.Common.Services;
using Dalamud.Plugin.Services;
using Ocelot.Actions;
using Ocelot.Extensions;
using Ocelot.States.Flow;

namespace BOCCHI.Buff.StateMachine.Handlers;

public class CastingInquiringMindHandler(
    IObjectTable objects,
    ISupportJobChanger changer,
    ISupportJobFactory supportJobs
) : FlowStateHandler<BuffState>(BuffState.CastingInquiringMind)
{
    private DateTime lastCast = DateTime.MinValue;

    public override void Enter()
    {
        base.Enter();
        lastCast = DateTime.MinValue - TimeSpan.FromSeconds(3);
    }

    public override BuffState? Handle()
    {
        if (objects.LocalPlayer == null)
        {
            return null;
        }

        if (GetMinutesRemainingForLowestBuff() >= 29)
        {
            return BuffState.ChoosingBuffToApply;
        }

        if (!supportJobs.TryGetCurrent(out var supportJob) && supportJob.Id == SupportJobId.PhantomFreelancer)
        {
            if (!changer.IsBusy())
            {
                changer.Change(SupportJobId.PhantomFreelancer);
            }

            return null;
        }

        var time = DateTime.UtcNow - lastCast;
        if (Actions.PhantomActionIII.CanCast() && time.TotalSeconds >= 3)
        {
            lastCast = DateTime.UtcNow;
            Actions.PhantomActionIII.Cast();
        }

        return null;
    }

    private uint GetMinutesRemainingForLowestBuff()
    {
        if (objects.LocalPlayer is not { } player)
        {
            return 0;
        }

        // @TODO replace .All with IBuffProvider.GetBuffs()
        uint lowest = 30;
        foreach (var buff in BuffData.All)
        {
            if (!player.StatusList.TryGet(buff.StatusId, out var status))
            {
                return 0;
            }


            var time = (uint)TimeSpan.FromSeconds(status.RemainingTime).TotalMinutes;
            if (time < lowest)
            {
                lowest = time;
            }
        }

        return lowest;
    }
}
