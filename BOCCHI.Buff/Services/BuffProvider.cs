using BOCCHI.Buff.Data;
using BOCCHI.Common.Config;
using BOCCHI.Common.Data.SupportJobs;
using Dalamud.Plugin.Services;
using Lumina.Extensions;
using Ocelot.Extensions;
using Ocelot.Services.PlayerState;

namespace BOCCHI.Buff.Services;

public class BuffProvider(
    IObjectTable objects,
    BuffConfig config,
    ISupportJobFactory supportJobs
) : IBuffProvider
{
    public IEnumerable<BuffData> GetBuffs()
    {
        return
        [
            BuffData.RomeosBallad,
            BuffData.Fleetfooted,
            BuffData.EnduringFortitude,
            BuffData.QuickerStep,
        ];
    }

    public BuffData GetBuffForState(BuffState state)
    {
        var buff = GetBuffs().FirstOrNull(b => b.State == state);
        if (buff == null)
        {
            throw new ArgumentOutOfRangeException();
        }

        return buff.Value;
    }

    public bool ShouldRefreshAny()
    {
        return GetBuffs().Any(ShouldRefreshBuff);
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

    private bool CanRefreshBuff(BuffData buff)
    {
        var job = supportJobs.Create(buff.SupportJobId);

        return job.Level >= buff.RequiredLevel;
    }

    private bool ShouldRefreshBuff(BuffData buff)
    {
        return CanRefreshBuff(buff) && GetMinutesRemainingForBuff(buff) <= config.ReapplyThreshold;
    }
}
