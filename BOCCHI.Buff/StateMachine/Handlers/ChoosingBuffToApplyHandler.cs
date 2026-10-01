using BOCCHI.Buff.Data;
using BOCCHI.Buff.Services;
using BOCCHI.Common.Config;
using BOCCHI.Common.Data.StateMemory;
using BOCCHI.Common.Data.SupportJobs;
using BOCCHI.Common.Extensions;
using BOCCHI.Common.Services;
using Dalamud.Plugin.Services;
using Ocelot.States.Flow;

namespace BOCCHI.Buff.StateMachine.Handlers;

public class ChoosingBuffToApplyHandler
(
    BuffConfig config,
    IObjectTable objects,
    IBuffProvider buffs,
    IAutomatorMemory memory,
    ISupportJobFactory supportJobs
) : FlowStateHandler<BuffState>(BuffState.ChoosingBuffToApply)
{
    private const uint ManualFreshEnoughMinutes = 25;

    public override BuffState? Handle()
    {
        if (objects.LocalPlayer is not { } player)
        {
            return null;
        }

        bool forceRefresh = memory.TryRemember<ManualBuffRunMemory>(out ManualBuffRunMemory _);
        uint maxFreshMinutes = forceRefresh ? ManualFreshEnoughMinutes : (uint)config.ReapplyThreshold;
        bool inquired = memory.TryRemember<InquiringMindAttemptedMemory>(out InquiringMindAttemptedMemory _);

        if (!inquired && buffs.NeedsInquiringMind(player, maxFreshMinutes))
        {
            return BuffState.CastingInquiringMind;
        }

        foreach (BuffData buff in buffs.GetBuffs().Where(b => b.ShouldApply(config)))
        {
            SupportJob job = supportJobs.Create(buff.SupportJobId);
            if (job.Level < buff.RequiredLevel)
            {
                continue;
            }

            uint remaining = player.GetRemainingMinutes(buff.StatusId);
            if (remaining > maxFreshMinutes)
            {
                continue;
            }

            return buff.State;
        }

        memory.Forget<ApplyingBuffsMemory>();
        memory.Forget<ManualBuffRunMemory>();
        memory.Forget<InquiringMindAttemptedMemory>();

        return null;
    }
}
