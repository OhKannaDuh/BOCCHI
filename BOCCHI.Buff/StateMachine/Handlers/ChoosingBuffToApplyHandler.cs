using System.Security.AccessControl;
using BOCCHI.Buff.Data;
using BOCCHI.Buff.Services;
using BOCCHI.Common.Config;
using BOCCHI.Common.Data.StateMemory;
using BOCCHI.Common.Data.SupportJobs;
using BOCCHI.Common.Services;
using Dalamud.Plugin.Services;
using Ocelot.Extensions;
using Ocelot.Services.PlayerState;
using Ocelot.States.Flow;

namespace BOCCHI.Buff.StateMachine.Handlers;

public class ChoosingBuffToApplyHandler(
    BuffConfig config,
    IObjectTable objects,
    IBuffProvider buffs,
    IAutomatorMemory memory,
    ISupportJobFactory supportJobs
) : FlowStateHandler<BuffState>(BuffState.ChoosingBuffToApply)
{
    public override BuffState? Handle()
    {
        if (objects.LocalPlayer == null)
        {
            return null;
        }

        foreach (var buff in buffs.GetBuffs().Where(b => b.ShouldApply(config)))
        {
            var job = supportJobs.Create(buff.SupportJobId);
            if (job.Level < buff.RequiredLevel)
            {
                continue;
            }

            var remaining = GetMinutesRemainingForBuff(buff);
            if (remaining > config.ReapplyThreshold)
            {
                continue;
            }

            var freelancer = supportJobs.Create(SupportJobId.PhantomFreelancer);
            if (config.ApplyBuffsUsingInquiringMind && freelancer.Level >= 15)
            {
                return BuffState.CastingInquiringMind;
            }

            return buff.State;
        }

        memory.Forget<ApplyingBuffsMemory>();

        return null;
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
}
