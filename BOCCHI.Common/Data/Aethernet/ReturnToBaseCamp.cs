using BOCCHI.Common.Data.Zones;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI;
using Ocelot.Chain;
using Ocelot.Chain.Extensions;
using Ocelot.Ipc.VNavmesh;
using Ocelot.Services.Pathfinding;

namespace BOCCHI.Common.Data.Aethernet;

public static class ReturnToBaseCamp
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(120);

    public static IChain Append(
        IChain chain,
        IZoneProvider zones,
        ICondition conditions,
        IGameGui gui,
        IPathfinder pathfinder,
        IVNavmeshIpc vnav)
    {
        string chainName = chain.Name;

        return chain
            .Then(_ =>
                {
                    pathfinder.Stop();
                    vnav.Stop();

                    if (zones.GetZone().IsInBasecamp())
                    {
                        return StepResult.Success();
                    }

                    if (conditions[ConditionFlag.Unconscious])
                    {
                        return StepResult.Failure("Cannot Return while unconscious.");
                    }

                    if (conditions[ConditionFlag.InCombat])
                    {
                        return StepResult.Failure("Cannot Return while in combat.");
                    }

                    if (DismountAssist.TryDismount(conditions))
                    {
                        return StepResult.Success();
                    }

                    if (OccultReturn.CanCast())
                    {
                        OccultReturn.Cast();
                    }

                    return StepResult.Success();
                }, $"{chainName}::CastReturn")
            .WaitUntil(
                _ =>
                {
                    if (zones.GetZone().IsInBasecamp())
                    {
                        return ValueTask.FromResult(true);
                    }

                    if (conditions[ConditionFlag.Unconscious] || conditions[ConditionFlag.InCombat])
                    {
                        return ValueTask.FromResult(false);
                    }

                    TryConfirmReturnDialog(gui, conditions);

                    if (DismountAssist.TryDismount(conditions))
                    {
                        return ValueTask.FromResult(false);
                    }

                    if (OccultReturn.CanCast())
                    {
                        OccultReturn.Cast();
                    }

                    return ValueTask.FromResult(false);
                },
                Timeout,
                TimeSpan.FromMilliseconds(250),
                $"{chainName}::WaitForBasecamp");
    }

    private static unsafe void TryConfirmReturnDialog(IGameGui gui, ICondition conditions)
    {
        if (conditions[ConditionFlag.Unconscious])
        {
            return;
        }

        AddonSelectYesno* yesno = gui.GetAddonByName<AddonSelectYesno>("SelectYesno");
        if (yesno == null)
        {
            return;
        }

        ReturnYesNo.TryAccept(&yesno->AtkUnitBase);
    }
}
