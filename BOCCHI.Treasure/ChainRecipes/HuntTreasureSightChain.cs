using BOCCHI.Common.Data.SupportJobs;
using BOCCHI.Common.Data.Zones;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using ECommons.DalamudServices;
using ECommons.Throttlers;
using FFXIVClientStructs.FFXIV.Client.Game.InstanceContent;
using Ocelot.Actions;
using Ocelot.Chain;
using Ocelot.Chain.Extensions;
using Ocelot.Chain.Middleware.Chain;
using Ocelot.Chain.Middleware.Step;
using Ocelot.Extensions;

namespace BOCCHI.Treasure.ChainRecipes;

public class HuntTreasureSightChain
(
    IChainFactory chains,
    ICondition conditions,
    ISupportJobFactory supportJobs,
    IObjectTable objects
) : ChainRecipe(chains)
{
    private static readonly TimeSpan SightCooldownGrace = TimeSpan.FromSeconds(4);

    public override string Name => "Hunt Treasure Sight";

    protected override IChain Compose(IChain chain)
    {
        SupportJob freelancer = supportJobs.Create(SupportJobId.PhantomFreelancer);

        // The recipe is shared by every run; per-run values live here so a new run cannot wipe an older one's.
        var castState = new CastState();

        return chain
            .UseMiddleware<LogChainMiddleware>()
            .UseMiddleware(new RestorePhantomJobMiddleware(this, castState))
            .UseStepMiddleware<LogStepMiddleware>()
            .UseStepMiddleware<RunOnMainThreadMiddleware>()
            .IfThen(
                _ =>
                {
                    CaptureRestoreIfNeeded(castState);
                    return !SupportJobTreasureSight.CanCast(supportJobs);
                },
                _ => ValueTask.FromResult(StepResult.Break()),
                "HuntTreasureSight::FreelancerTooLow"
            )
            .WaitUntil(
                _ =>
                {
                    CaptureRestoreIfNeeded(castState);
                    return ValueTask.FromResult(IsOnFoot());
                },
                TimeSpan.FromSeconds(15),
                TimeSpan.FromMilliseconds(250),
                "HuntTreasureSight::Dismount"
            )
            .WaitUntil(
                _ =>
                {
                    CaptureRestoreIfNeeded(castState);
                    return ValueTask.FromResult(TryBecomeJob(SupportJobId.PhantomFreelancer, freelancer.StatusId));
                },
                TimeSpan.FromSeconds(15),
                TimeSpan.FromMilliseconds(250),
                "HuntTreasureSight::ToFreelancer"
            )
            .WaitUntil(
                _ => ValueTask.FromResult(TryCastSight(castState)),
                TimeSpan.FromSeconds(20),
                TimeSpan.FromMilliseconds(250),
                "HuntTreasureSight::Cast"
            )
            .WaitUntil(
                _ => ValueTask.FromResult(TryRestore(castState.RestoreAfterSight)),
                TimeSpan.FromSeconds(15),
                TimeSpan.FromMilliseconds(250),
                "HuntTreasureSight::RestoreJob"
            );
    }

    private bool IsOnFoot() => !DismountAssist.TryDismount(conditions);

    private void CaptureRestoreIfNeeded(CastState state)
    {
        if (state.RestoreCaptured)
        {
            return;
        }

        state.RestoreCaptured = true;
        if (supportJobs.TryGetCurrent(out SupportJob current)
            && current.Id != SupportJobId.PhantomFreelancer)
        {
            state.RestoreAfterSight = current.Id;
        }
    }

    private bool TryBecomeJob(SupportJobId id, uint statusId)
    {
        if (objects.LocalPlayer is not { } player)
        {
            return false;
        }

        if (supportJobs.TryGetCurrent(out SupportJob current) && current.Id == id)
        {
            return true;
        }

        if (PhantomJobChangeGate.IsBlocked(conditions))
        {
            return false;
        }

        if (!EzThrottler.Throttle($"HuntTreasureSight::Change::{id}", 750))
        {
            return false;
        }

        unsafe
        {
            PublicContentOccultCrescent.ChangeSupportJob((byte)id);
        }

        return player.StatusList.Has(statusId);
    }

    private bool TryCastSight(CastState state)
    {
        CaptureRestoreIfNeeded(state);

        if (IsCasting())
        {
            state.SawCasting = true;
            state.CdBlockedSinceUtc = null;
            return false;
        }

        if (state.SawCasting || state.Issued)
        {
            return true;
        }

        if (DismountAssist.TryDismount(conditions))
        {
            return false;
        }

        if (!EzThrottler.Throttle("HuntTreasureSight::Cast", 500))
        {
            return false;
        }

        if (!Actions.PhantomActionII.CanCast())
        {
            state.CdBlockedSinceUtc ??= DateTime.UtcNow;
            if (DateTime.UtcNow - state.CdBlockedSinceUtc.Value >= SightCooldownGrace)
            {
                state.SightCastSkippedCd = true;
                return true;
            }

            return false;
        }

        state.CdBlockedSinceUtc = null;

        if (Actions.PhantomActionII.Cast())
        {
            state.Issued = true;
        }

        return false;
    }

    private bool IsCasting() =>
        conditions[ConditionFlag.Casting] || conditions[ConditionFlag.Casting87];

    private bool TryRestore(SupportJobId? restoreId)
    {
        // Never swap while still casting — job change cancels Treasure Sight.
        if (IsCasting())
        {
            return false;
        }

        if (restoreId is not { } id)
        {
            return true;
        }

        SupportJob job = supportJobs.Create(id);
        return TryBecomeJob(id, job.StatusId);
    }

    private void TryRestorePhantomJob(CastState state)
    {
        if (state.RestoreAfterSight is not { } id)
        {
            return;
        }

        if (supportJobs.TryGetCurrent(out SupportJob current) && current.Id != SupportJobId.PhantomFreelancer)
        {
            return;
        }

        SupportJob job = supportJobs.Create(id);
        TryBecomeJob(id, job.StatusId);
    }

    private sealed class RestorePhantomJobMiddleware(HuntTreasureSightChain owner, CastState state) : IChainMiddleware
    {
        public async Task<ChainResult> InvokeAsync(IChainContext context, ChainMiddlewareDelegate next)
        {
            ChainResult result;
            try
            {
                result = await next();
            }
            finally
            {
                // After the last await this can be any thread; the job change must run on the game thread.
                await Svc.Framework.RunOnFrameworkThread(() => owner.TryRestorePhantomJob(state));
            }

            if (state.SightCastSkippedCd)
            {
                return ChainResult.Failure("Treasure Sight on cooldown");
            }

            return result;
        }
    }

    private sealed class CastState
    {
        public SupportJobId? RestoreAfterSight;

        public bool RestoreCaptured;

        public bool SightCastSkippedCd;

        public DateTime? CdBlockedSinceUtc;

        public bool Issued;

        public bool SawCasting;
    }
}
