using System.Collections.Generic;
using System.Linq;
using BOCCHI.Data;
using BOCCHI.Modules.Buff.Chains;
using ECommons.GameHelpers;
using ECommons.Throttlers;
using Ocelot.Chain;

namespace BOCCHI.Modules.Buff;

public class BuffManager
{
    private bool applyBuffsOnNextTick = false;

    public void QueueBuffs()
    {
        applyBuffsOnNextTick = true;
    }

    public bool IsQueued()
    {
        return applyBuffsOnNextTick;
    }

    private int lowestTimer = int.MaxValue;

    public void Update(BuffModule module)
    {
        if (applyBuffsOnNextTick)
        {
            applyBuffsOnNextTick = false;
            ApplyBuffs(module);
        }

        if (EzThrottler.Throttle("BuffManager.Tick.GetLowestBuffTimer", 1000))
        {
            lowestTimer = GetLowestBuffTimer(module);
        }
    }

    public void ApplyBuffs(BuffModule module)
    {
        var manager = ChainManager.Get("OCH##BuffManager");
        if (manager.IsRunning)
        {
            return;
        }

        manager.Submit(new AllBuffsChain(module));
    }

    private int GetLowestBuffTimer(BuffModule module)
    {
        List<uint> buffs = [];

        if (module.Config.ApplyEnduringFortitude)
        {
            buffs.Add((uint)PlayerStatus.EnduringFortitude);
        }

        if (module.Config.ApplyFleetfooted)
        {
            buffs.Add((uint)PlayerStatus.Fleetfooted);
        }

        if (module.Config.ApplyRomeosBallad)
        {
            buffs.Add((uint)PlayerStatus.RomeosBallad);
        }

        if (module.Config.ApplyQuickerStep)
        {
            buffs.Add((uint)PlayerStatus.QuickerStep);
        }

        var statuses = Player.Status.Where(s => buffs.Contains(s.StatusId)).ToList();
        return statuses.Count == 0 ? 0 : statuses.Select(status => (int)status.RemainingTime).Min();
    }

    public bool ShouldRefresh(BuffModule module)
    {
        if (!module.IsEnabled)
        {
            return false;
        }

        return lowestTimer <= module.Config.ReapplyThreshold * 60;
    }
}
