using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using ECommons.UIHelpers.AddonMasterImplementations;
using FFXIVClientStructs.FFXIV.Client.UI;
using Ocelot.Actions;
using Ocelot.Chain;
using Ocelot.Chain.Extensions;
using Ocelot.Chain.Middleware.Step;

namespace BOCCHI.Services.Repair;

public class RepairStep(
    IChainFactory chains,
    ICondition condition,
    IGameGui gui
) : ChainRecipe(chains)
{
    public override string Name { get; } = "Repair";

    protected override IChain Compose(IChain chain)
    {
        return chain
            .UseStepMiddleware(new RetryStepMiddleware
            {
                DelayMs = 100,
                MaxAttempts = 30,
            })
            .Then(_ =>
            {
                if (condition[ConditionFlag.Occupied39])
                {
                    return StepResult.Failure("Busy");
                }

                unsafe
                {
                    var repair = gui.GetAddonByName<AddonRepair>("Repair", 1);
                    if (repair == null || !repair->AtkUnitBase.IsVisible || !repair->RepairAllButton->IsEnabled)
                    {
                        Actions.Repair.Cast();
                    }

                    repair = gui.GetAddonByName<AddonRepair>("Repair", 1);
                    if (repair == null || !repair->AtkUnitBase.IsVisible || !repair->RepairAllButton->IsEnabled)
                    {
                        return StepResult.Failure("Repair not open");
                    }

                    return StepResult.Success();
                }
            }, "Repair::Open")
            .Then(_ =>
            {
                if (condition[ConditionFlag.Occupied39])
                {
                    return StepResult.Failure("Busy");
                }

                unsafe
                {
                    var repair = gui.GetAddonByName<AddonRepair>("Repair", 1);
                    if (repair == null || !repair->AtkUnitBase.IsVisible || !repair->RepairAllButton->IsEnabled)
                    {
                        return StepResult.Failure("Repair Addon not found");
                    }

                    new AddonMaster.Repair((IntPtr)repair).RepairAll();

                    return StepResult.Success();
                }
            }, "Repair::Repair")
            .Then(_ =>
            {
                if (condition[ConditionFlag.Occupied39])
                {
                    return StepResult.Failure("Busy");
                }

                unsafe
                {
                    var repair = gui.GetAddonByName<AddonRepair>("Repair", 1);
                    if (repair == null || !repair->AtkUnitBase.IsVisible || !repair->RepairAllButton->IsEnabled)
                    {
                        return StepResult.Failure("Repair Addon not found");
                    }

                    var yesno = gui.GetAddonByName<AddonSelectYesno>("SelectYesno", 1);
                    if (yesno == null || !yesno->AtkUnitBase.IsVisible || !yesno->AtkUnitBase.UldManager.NodeList[15]->IsVisible())
                    {
                        return StepResult.Failure("SelectYesno Addon not found");
                    }

                    try
                    {
                        new AddonMaster.SelectYesno((IntPtr)yesno).Yes();
                        return StepResult.Success();
                    }
                    catch (Exception ex)
                    {
                        return StepResult.Failure(ex);
                    }
                }
            }, "Repair::Yes");
    }
}