using BOCCHI.Buff.Data;
using BOCCHI.Common.Config;
using BOCCHI.Common.Data.OccultCrescent;
using BOCCHI.Common.Data.SupportJobs;
using BOCCHI.Common.Extensions;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Plugin.Services;
using Lumina.Extensions;

namespace BOCCHI.Buff.Services;

public class BuffProvider
(
    IObjectTable objects,
    BuffConfig config,
    ISupportJobFactory supportJobs
) : IBuffProvider
{
    public const uint InquiringMindFreshMinutes = 29;

    public IEnumerable<BuffData> GetBuffs() => BuffData.All;

    public BuffData GetBuffForState(BuffState state)
    {
        BuffData? buff = GetBuffs().FirstOrNull(b => b.State == state);
        if (buff == null)
        {
            throw new ArgumentOutOfRangeException();
        }

        return buff.Value;
    }

    public bool ShouldRefreshAny()
    {
        if (objects.LocalPlayer is not { } player)
        {
            return false;
        }

        if (GetBuffs().Any(ShouldRefreshBuff))
        {
            return true;
        }

        return CanUseInquiringMind()
               && GetInquiringMindTargets().Any(b =>
                   player.GetRemainingMinutes(b.StatusId) <= (uint)config.ReapplyThreshold);
    }

    public bool CanUseInquiringMind()
    {
        if (!config.ApplyBuffsUsingInquiringMind)
        {
            return false;
        }

        SupportJob freelancer = supportJobs.Create(SupportJobId.PhantomFreelancer);
        return freelancer.Level >= PhantomActions.InquiringMindUnlock;
    }

    public IEnumerable<BuffData> GetInquiringMindTargetsNeedingRefresh(IPlayerCharacter player, uint maxFreshMinutes) =>
        GetInquiringMindTargets()
            .Where(b => player.GetRemainingMinutes(b.StatusId) <= maxFreshMinutes);

    public bool NeedsInquiringMind(IPlayerCharacter player, uint maxFreshMinutes) =>
        CanUseInquiringMind()
        && GetInquiringMindTargetsNeedingRefresh(player, maxFreshMinutes).Any();

    public bool AreInquiringMindTargetsFresh(IPlayerCharacter player)
    {
        List<BuffData> targets = GetInquiringMindTargets().ToList();
        if (targets.Count == 0)
        {
            return true;
        }

        return targets.All(b => player.GetRemainingMinutes(b.StatusId) >= InquiringMindFreshMinutes);
    }

    private IEnumerable<BuffData> GetInquiringMindTargets()
    {
        if (!CanUseInquiringMind())
        {
            return [];
        }

        List<BuffData> selected = GetBuffs().Where(b => b.ShouldApply(config) && CanRefreshBuff(b)).ToList();
        if (selected.Count > 0)
        {
            return selected;
        }

        return GetBuffs().Where(CanRefreshBuff);
    }

    private bool CanRefreshBuff(BuffData buff)
    {
        SupportJob job = supportJobs.Create(buff.SupportJobId);
        return job.Level >= buff.RequiredLevel;
    }

    private bool ShouldRefreshBuff(BuffData buff)
    {
        if (objects.LocalPlayer is not { } player)
        {
            return false;
        }

        return buff.ShouldApply(config)
               && CanRefreshBuff(buff)
               && player.GetRemainingMinutes(buff.StatusId) <= config.ReapplyThreshold;
    }
}
