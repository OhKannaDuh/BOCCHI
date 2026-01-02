using Ocelot.Actions;
using Ocelot.Chain;
using Ocelot.Chain.Extensions;
using Ocelot.Chain.Middleware.Step;
using Ocelot.Services.PlayerState;

namespace BOCCHI.Common.Steps;

public class UnmountStep(IChainFactory chains, IPlayer player) : ChainRecipe(chains)
{
    public override string Name { get; } = "Unmount";

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
                if (player.IsMounted())
                {
                    Actions.Unmount.Cast();
                }

                return player.IsMounted() ? StepResult.Failure("Unable to unmount") : StepResult.Success();
            }, "Unmount::Unmount");
    }
}