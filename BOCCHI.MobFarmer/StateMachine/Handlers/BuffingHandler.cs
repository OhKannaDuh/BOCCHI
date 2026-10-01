using BOCCHI.Common.Config;
using BOCCHI.Common.Data.OccultCrescent;
using BOCCHI.Common.Data.SupportJobs;
using BOCCHI.Common.Data.Zones;
using BOCCHI.Common.Extensions;
using BOCCHI.Common.Services;
using BOCCHI.MobFarmer.Data;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using Ocelot.Actions;
using Ocelot.Extensions;
using Ocelot.States.Flow;
using Action = Ocelot.Actions.Action;

namespace BOCCHI.MobFarmer.StateMachine.Handlers;

public class BuffingHandler
(
    MobFarmerConfig config,
    ICondition conditions,
    IObjectTable objects,
    ISupportJobFactory supportJobs,
    ISupportJobChanger changer
) : FlowStateHandler<FarmerPhase>(FarmerPhase.Buffing)
{
    private static Action BattleBell => new(ActionType.Action, PhantomActions.BattleBell);

    private static Action RingingRespite => new(ActionType.Action, PhantomActions.RingingRespite);

    private static Action Counterstance => new(ActionType.Action, PhantomActions.Counterstance);

    private static readonly TimeSpan StepGiveUp = TimeSpan.FromSeconds(2.5);

    private bool quickstepDone;

    private bool quickstepIssued;

    private bool bellDone;

    private bool respiteDone;

    private bool respiteIssued;

    private bool counterstanceDone;

    private bool counterstanceIssued;

    private bool sprintDone;

    private DateTimeOffset? stepWaitStartedUtc;

    private SupportJobId? jobToRestore;

    public override void Enter()
    {
        base.Enter();
        quickstepDone = false;
        quickstepIssued = false;
        bellDone = false;
        respiteDone = false;
        respiteIssued = false;
        counterstanceDone = false;
        counterstanceIssued = false;
        sprintDone = false;
        stepWaitStartedUtc = null;
        jobToRestore = null;

        if (supportJobs.TryGetCurrent(out SupportJob job))
        {
            jobToRestore = job.Id;
        }
    }

    public override FarmerPhase? Handle()
    {
        if (DismountAssist.TryDismount(conditions))
        {
            return null;
        }

        if (!quickstepDone)
        {
            TryQuickstep();
            if (!quickstepDone)
            {
                return null;
            }
        }

        if (!bellDone || !respiteDone)
        {
            TryGeomancerBuffs();
            if (!bellDone || !respiteDone)
            {
                return null;
            }
        }

        if (!counterstanceDone)
        {
            TryCounterstance();
            if (!counterstanceDone)
            {
                return null;
            }
        }

        return TrySprintThenGather();
    }

    private void TryQuickstep()
    {
        if (!config.ApplyQuickstep || supportJobs.Create(SupportJobId.PhantomDancer).Level < PhantomActions.QuickstepUnlock)
        {
            FinishQuickstep();
            return;
        }

        if (config.QuickstepSkipIfRemainingMinutes > 0
            && objects.LocalPlayer is { } local
            && local.GetRemainingMinutes(PhantomBuffs.QuickerStep) >= (uint)config.QuickstepSkipIfRemainingMinutes)
        {
            FinishQuickstep();
            return;
        }

        if (!IsJob(SupportJobId.PhantomDancer))
        {
            if (!changer.IsBusy())
            {
                changer.Change(SupportJobId.PhantomDancer);
            }

            return;
        }

        if (quickstepIssued)
        {
            if (HasQuickstepBuff() || DateTimeOffset.UtcNow - (stepWaitStartedUtc ?? DateTimeOffset.UtcNow) >= StepGiveUp)
            {
                FinishQuickstep();
            }

            return;
        }

        if (Actions.PhantomActionII.CanCast() && Actions.PhantomActionII.Cast())
        {
            quickstepIssued = true;
            stepWaitStartedUtc = DateTimeOffset.UtcNow;
            return;
        }

        if (Actions.PhantomActionII.GetRecastTime() > 0f)
        {
            FinishQuickstep();
            return;
        }

        stepWaitStartedUtc ??= DateTimeOffset.UtcNow;
        if (DateTimeOffset.UtcNow - stepWaitStartedUtc.Value >= StepGiveUp)
        {
            FinishQuickstep();
        }
    }

    private void FinishQuickstep()
    {
        quickstepDone = true;
        stepWaitStartedUtc = null;
    }

    private void TryGeomancerBuffs()
    {
        bool wantBell = config.ApplyBattleBell && BattleBell.GetRecastTime() <= config.MaximumBattleBellWaitTime;
        bool wantRespite = config.ApplyRingingRespite
                           && supportJobs.Create(SupportJobId.PhantomGeomancer).Level
                           >= PhantomActions.RingingRespiteUnlock;

        if (!wantBell)
        {
            bellDone = true;
        }

        if (!wantRespite)
        {
            respiteDone = true;
        }

        if (bellDone && respiteDone)
        {
            return;
        }

        if (!IsJob(SupportJobId.PhantomGeomancer))
        {
            if (!changer.IsBusy())
            {
                changer.Change(SupportJobId.PhantomGeomancer);
            }

            return;
        }

        if (!bellDone)
        {
            if (BattleBell.GetRecastTime() <= 0f && Actions.PhantomActionI.CanCast())
            {
                Actions.PhantomActionI.Cast();
                return;
            }

            if (!HasBattleBell())
            {
                return;
            }

            bellDone = true;
        }

        if (!respiteDone)
        {
            float respiteCd = RingingRespite.GetRecastTime();
            if (respiteCd > config.MaximumBattleBellWaitTime)
            {
                respiteDone = true;
                stepWaitStartedUtc = null;
                return;
            }

            if (respiteIssued)
            {
                if (respiteCd > 0f
                    || HasRingingRespite()
                    || DateTimeOffset.UtcNow - (stepWaitStartedUtc ?? DateTimeOffset.UtcNow) >= StepGiveUp)
                {
                    respiteDone = true;
                    stepWaitStartedUtc = null;
                }

                return;
            }

            if (respiteCd > 0f)
            {
                return;
            }

            if (Actions.PhantomActionIII.CanCast() && Actions.PhantomActionIII.Cast())
            {
                respiteIssued = true;
                stepWaitStartedUtc = DateTimeOffset.UtcNow;
                return;
            }

            stepWaitStartedUtc ??= DateTimeOffset.UtcNow;
            if (DateTimeOffset.UtcNow - stepWaitStartedUtc.Value >= StepGiveUp)
            {
                respiteDone = true;
                stepWaitStartedUtc = null;
            }
        }
    }

    private void TryCounterstance()
    {
        if (!config.ApplyCounterstance
            || supportJobs.Create(SupportJobId.PhantomMonk).Level < PhantomActions.CounterstanceUnlock)
        {
            counterstanceDone = true;
            return;
        }

        // Already issued this pull — wait for Fleetfooted or give up. Counterstance is
        // GCD-only; re-casting every GCD when the buff check fails is the spam bug.
        if (counterstanceIssued)
        {
            if (HasFleetfooted() || DateTimeOffset.UtcNow - (stepWaitStartedUtc ?? DateTimeOffset.UtcNow) >= StepGiveUp)
            {
                counterstanceDone = true;
                counterstanceIssued = false;
                stepWaitStartedUtc = null;
            }

            return;
        }

        float cd = Counterstance.GetRecastTime();
        if (cd > config.MaximumBattleBellWaitTime)
        {
            counterstanceDone = true;
            return;
        }

        if (cd > 0f)
        {
            return;
        }

        if (!IsJob(SupportJobId.PhantomMonk))
        {
            if (!changer.IsBusy())
            {
                changer.Change(SupportJobId.PhantomMonk);
            }

            return;
        }

        if (HasFleetfooted())
        {
            counterstanceDone = true;
            return;
        }

        if (Actions.PhantomActionIII.CanCast() && Actions.PhantomActionIII.Cast())
        {
            counterstanceIssued = true;
            stepWaitStartedUtc = DateTimeOffset.UtcNow;
        }
    }

    private FarmerPhase? TrySprintThenGather()
    {
        bool appliedAny = config.ApplyQuickstep
                          || config.ApplyBattleBell
                          || config.ApplyRingingRespite
                          || config.ApplyCounterstance;
        if (!sprintDone && appliedAny)
        {
            stepWaitStartedUtc ??= DateTimeOffset.UtcNow;

            if (Actions.Sprint.CanCast())
            {
                Actions.Sprint.Cast();
                return null;
            }

            bool sprintOnCooldown = Actions.Sprint.GetRecastTime() > 0f;
            bool timedOut = DateTimeOffset.UtcNow - stepWaitStartedUtc >= StepGiveUp;
            if (!sprintOnCooldown && !timedOut)
            {
                return null;
            }

            sprintDone = true;
            stepWaitStartedUtc = null;
        }

        return RestoreThenGather();
    }

    private FarmerPhase? RestoreThenGather()
    {
        if (jobToRestore is not { } restoreId)
        {
            return FarmerPhase.Gathering;
        }

        if (supportJobs.TryGetCurrent(out SupportJob current) && current.Id == restoreId)
        {
            jobToRestore = null;
            return FarmerPhase.Gathering;
        }

        if (!changer.IsBusy())
        {
            changer.Change(restoreId);
        }

        return null;
    }

    private bool IsJob(SupportJobId id) =>
        supportJobs.TryGetCurrent(out SupportJob job) && job.Id == id;

    private bool HasBattleBell()
    {
        if (objects.LocalPlayer is not { } player)
        {
            return false;
        }

        return player.StatusList.Has(PhantomBuffs.BattleBell)
               || player.StatusList.Has(PhantomBuffs.BattlesClangor);
    }

    private bool HasRingingRespite()
    {
        if (objects.LocalPlayer is not { } player)
        {
            return false;
        }

        return player.StatusList.Has(PhantomBuffs.RingingRespite);
    }

    private bool HasQuickstepBuff()
    {
        if (objects.LocalPlayer is not { } player)
        {
            return false;
        }

        return player.StatusList.Has(PhantomBuffs.QuickerStep);
    }

    private bool HasFleetfooted()
    {
        if (objects.LocalPlayer is not { } player)
        {
            return false;
        }

        return player.StatusList.Has(PhantomBuffs.Fleetfooted);
    }
}
