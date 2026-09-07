using System.Numerics;
using BOCCHI.Common.Services;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using ECommons.Throttlers;
using ECommons.UIHelpers.AddonMasterImplementations;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.UI;
using Ocelot.Chain;
using Ocelot.Chain.Extensions;
using Ocelot.Chain.Middleware.Step;
using Ocelot.Extensions;
using Ocelot.Ipc.VNavmesh;
using Ocelot.Services.PlayerState;

namespace BOCCHI.Services.Repair;

/// <summary>
///     Artisan / AutoDuty flow: path to a nearby mender, open Repair via SelectIconString, Repair All.
/// </summary>
public class NpcRepairStep
(
    IChainFactory chains,
    ICondition condition,
    IGameGui gui,
    IObjectTable objects,
    IDataManager data,
    IPlayer player,
    IVNavmeshIpc vnav,
    IRepairService repairService
) : ChainRecipe(chains)
{
    public override string Name => "NpcRepair";

    protected override IChain Compose(IChain chain)
    {
        return chain
            .UseStepMiddleware(new RetryStepMiddleware
            {
                DelayMs = 250,
                MaxAttempts = 120
            })
            .Then(_ => Tick(), "NpcRepair::Run");
    }

    private unsafe StepResult Tick()
    {
        if (condition[ConditionFlag.Occupied39]
            && !condition[ConditionFlag.OccupiedInQuestEvent])
        {
            return StepResult.Failure("Busy");
        }

        AddonRepair* repair = gui.GetAddonByName<AddonRepair>("Repair");
        bool repairOpen = repair != null && repair->AtkUnitBase.IsVisible;

        if (repairOpen)
        {
            vnav.Stop();

            AddonSelectYesno* yesno = gui.GetAddonByName<AddonSelectYesno>("SelectYesno");
            if (yesno != null
                && yesno->AtkUnitBase.IsVisible
                && yesno->AtkUnitBase.UldManager.NodeList[15]->IsVisible())
            {
                if (!EzThrottler.Throttle("NpcRepair::Yes", 500))
                {
                    return StepResult.Failure("Wait Yes");
                }

                try
                {
                    new AddonMaster.SelectYesno((nint)yesno).Yes();
                }
                catch
                {
                    return StepResult.Failure("Yes failed");
                }

                return StepResult.Failure("Confirming");
            }

            if (!repair->RepairAllButton->IsEnabled)
            {
                // Button disabled while gear still needs repair = UI not ready / wrong state.
                // Do not treat as success (that was "walk to NPC, leave without repairing").
                if (repairService.ShouldRepair())
                {
                    return StepResult.Failure("RepairAll not ready");
                }

                if (EzThrottler.Throttle("NpcRepair::Close", 500))
                {
                    repair->AtkUnitBase.FireCallbackInt(-1);
                }

                return StepResult.Success();
            }

            if (!EzThrottler.Throttle("NpcRepair::All", 750))
            {
                return StepResult.Failure("Wait RepairAll");
            }

            new AddonMaster.Repair((nint)repair).RepairAll();
            return StepResult.Failure("RepairAll clicked");
        }

        nint selectAddr = gui.GetAddonByName("SelectIconString", 1).Address;
        if (selectAddr != nint.Zero)
        {
            vnav.Stop();
            if (!RepairNpc.TryFindNearby(objects, data, player.Position, out _, out int menuIndex)
                || menuIndex < 0)
            {
                return StepResult.Failure("No repair menu index");
            }

            if (!EzThrottler.Throttle("NpcRepair::Menu", 750))
            {
                return StepResult.Failure("Wait menu");
            }

            try
            {
                new AddonMaster.SelectIconString(selectAddr).Entries[menuIndex].Select();
            }
            catch
            {
                return StepResult.Failure("Menu select failed");
            }

            return StepResult.Failure("Menu selected");
        }

        if (!RepairNpc.TryFindNearby(
                objects,
                data,
                player.Position,
                out IGameObject npc,
                out _))
        {
            return StepResult.Failure("No mender nearby");
        }

        float dist = npc.Position.Distance2D(player.Position);
        if (dist > RepairNpc.InteractRadius)
        {
            if (vnav.IsNavmeshReady() && EzThrottler.Throttle("NpcRepair::Path", 1000))
            {
                Vector3 dest = npc.Position.GetApproachPosition(player.Position, 2.5f);
                vnav.PathfindAndMoveCloseTo(dest, false, 1.5f);
            }

            return StepResult.Failure("Approaching mender");
        }

        vnav.Stop();
        if (!EzThrottler.Throttle("NpcRepair::Interact", 1000))
        {
            return StepResult.Failure("Wait interact");
        }

        TargetSystem.Instance()->InteractWithObject(
            (FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject*)npc.Address,
            false);
        return StepResult.Failure("Interacted");
    }
}
