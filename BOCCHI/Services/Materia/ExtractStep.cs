using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using ECommons.Throttlers;
using ECommons.UIHelpers.AddonMasterImplementations;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Ocelot.Actions;
using Ocelot.Chain;
using Ocelot.Chain.Extensions;
using Ocelot.Chain.Middleware.Step;
using ValueType = FFXIVClientStructs.FFXIV.Component.GUI.ValueType;

namespace BOCCHI.Services.Materia;

public class ExtractStep(
    IChainFactory chains,
    ICondition condition,
    IGameGui gui
) : ChainRecipe(chains)
{
    public override string Name { get; } = "Extract";

    protected override IChain Compose(IChain chain)
    {
        return chain
            .UseStepMiddleware(new RetryStepMiddleware
            {
                DelayMs = 300,
                MaxAttempts = 10,
            })
            .Then(_ =>
            {
                if (condition[ConditionFlag.Occupied39])
                {
                    return StepResult.Failure("Busy");
                }

                if (EzThrottler.Throttle("ExtractStep::Extract::Extract", 100))
                {
                    return StepResult.Break();
                }

                unsafe
                {
                    var values = stackalloc AtkValue[2];
                    values[0] = new AtkValue
                    {
                        Type = ValueType.Int,
                        Int = 2,
                    };
                    values[1] = new AtkValue
                    {
                        Type = ValueType.UInt,
                        UInt = 0,
                    };

                    var materialize = (AtkUnitBase*)gui.GetAddonByName("Materialize", 1).Address;
                    if (materialize == null)
                    {
                        Actions.MateriaExtraction.Cast();
                        return StepResult.Break();
                    }

                    materialize->FireCallback(2, values);
                    return StepResult.Success();
                }
            }, "Extract::Callback")
            .Then(_ =>
            {
                if (condition[ConditionFlag.Occupied39] || gui.GetAddonByName("Materialize") == IntPtr.Zero)
                {
                    return StepResult.Failure("Busy");
                }

                var dialogPtr = gui.GetAddonByName("MaterializeDialog", 1);
                if (dialogPtr == null || dialogPtr == IntPtr.Zero)
                {
                    return StepResult.Break();
                }

                unsafe
                {
                    var dialog = (AtkUnitBase*)dialogPtr.Address;
                    if (dialog == null)
                    {
                        return StepResult.Break();
                    }

                    new AddonMaster.MaterializeDialog(dialog).Materialize();
                    return StepResult.Success();
                }
            }, "Extract::Confirm");
    }
}