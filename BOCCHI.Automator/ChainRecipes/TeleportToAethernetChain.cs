using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using Ocelot.Chain;
using Ocelot.Chain.Extensions;
using Ocelot.Chain.Middleware.Chain;
using Ocelot.Chain.Middleware.Step;
using Ocelot.Ipc.BossMod;
using Ocelot.Services.Logger;

namespace BOCCHI.Automator.ChainRecipes;

public class TeleportToAethernetChain(
    IChainFactory chains,
    ILifestreamIpc lifestream,
    ICondition conditions,
    ILogger<TeleportToAethernetChain> logger
) : ChainRecipe<uint>(chains)
{
    public override string Name { get; } = "Teleport to Aethernet Chain";

    protected override IChain Compose(IChain chain, uint id)
    {
        return chain
                .UseMiddleware<LogChainMiddleware>()
                .UseMiddleware(new RetryChainMiddleware(logger)
                {
                    DelayMs = 500,
                    MaxAttempts = 5,
                })
                .UseStepMiddleware<LogStepMiddleware>()
                .UseStepMiddleware<RunOnMainThreadMiddleware>()
                .WaitUntil(
                    _ => ValueTask.FromResult(!lifestream.IsBusy()),
                    TimeSpan.FromSeconds(3),
                    TimeSpan.FromMilliseconds(250),
                    "TeleportToAethernetChain::WaitUntilLifestreamIsFree"
                )
                .Then(
                    _ => lifestream.AethernetTeleportByPlaceNameId(id),
                    "TeleportToAethernetChain::Teleport"
                )

                .WaitUntil(
                    _ => ValueTask.FromResult(conditions[ConditionFlag.BetweenAreas] || conditions[ConditionFlag.BetweenAreas51]),
                    TimeSpan.FromSeconds(3),
                    TimeSpan.FromMilliseconds(250),
                    "TeleportToAethernetChain::WaitUntilBetweenAreas"
                )
                .WaitUntil(
                    _ => ValueTask.FromResult(!conditions[ConditionFlag.BetweenAreas] && !conditions[ConditionFlag.BetweenAreas51]),
                    TimeSpan.FromSeconds(3),
                    TimeSpan.FromMilliseconds(250),
                    "TeleportToAethernetChain::WaitUntilNotBetweenAreas"
                )
                .Wait(TimeSpan.FromMilliseconds(500));
    }
}
