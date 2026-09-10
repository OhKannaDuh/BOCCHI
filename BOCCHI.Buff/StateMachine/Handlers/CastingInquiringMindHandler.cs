using BOCCHI.Buff.Data;
using BOCCHI.Buff.Services;
using BOCCHI.Common.Data.StateMemory;
using BOCCHI.Common.Data.SupportJobs;
using BOCCHI.Common.Data.Zones;
using BOCCHI.Common.Services;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using Ocelot.Actions;
using Ocelot.States.Flow;

namespace BOCCHI.Buff.StateMachine.Handlers;

public class CastingInquiringMindHandler
(
    IObjectTable objects,
    ICondition conditions,
    ISupportJobChanger changer,
    ISupportJobFactory supportJobs,
    IBuffProvider buffs,
    IAutomatorMemory memory,
    IZoneProvider zones
) : FlowStateHandler<BuffState>(BuffState.CastingInquiringMind)
{
    private DateTime lastCast = DateTime.MinValue;
    private int castAttempts;

    public override void Enter()
    {
        base.Enter();
        lastCast = DateTime.MinValue;
        castAttempts = 0;
    }

    public override BuffState? Handle()
    {
        if (objects.LocalPlayer is not { } player)
        {
            return null;
        }

        // Inquiring Mind grants all unlocked crystal buffs in one cast — not Quicker Step only.
        if (buffs.AreInquiringMindTargetsFresh(player))
        {
            memory.TryAdd<InquiringMindAttemptedMemory>();
            return BuffState.ChoosingBuffToApply;
        }

        // Casts that never landed must not burn the 3-try budget (that fell through to ~40s).
        if (castAttempts >= 3)
        {
            memory.TryAdd<InquiringMindAttemptedMemory>();
            return BuffState.ChoosingBuffToApply;
        }

        IZone zone = zones.GetZone();
        if (!zone.IsInBuffCastRange(player.Position))
        {
            return BuffState.ApproachingKnowledgeCrystal;
        }

        if (DismountAssist.TryDismount(conditions))
        {
            return null;
        }

        if (PhantomJobChangeGate.IsBlocked(conditions) || changer.IsBusy())
        {
            return null;
        }

        if (!supportJobs.TryGetCurrent(out SupportJob supportJob) || supportJob.Id != SupportJobId.PhantomFreelancer)
        {
            changer.Change(SupportJobId.PhantomFreelancer);
            return null;
        }

        // First cast is in flight — wait for statuses before counting another miss.
        TimeSpan time = DateTime.UtcNow - lastCast;
        if (lastCast != DateTime.MinValue && time.TotalSeconds < 2)
        {
            return null;
        }

        if (Actions.PhantomActionIII.CanCast() && (lastCast == DateTime.MinValue || time.TotalSeconds >= 3))
        {
            lastCast = DateTime.UtcNow;
            castAttempts++;
            Actions.PhantomActionIII.Cast();
        }

        return null;
    }
}
