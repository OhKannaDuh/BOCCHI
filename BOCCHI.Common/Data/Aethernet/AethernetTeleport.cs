using BOCCHI.Common.Config;
using BOCCHI.Common.Data.Zones;
using Dalamud.Plugin.Services;
using Ocelot.Chain;
using Ocelot.Chain.Extensions;
using Ocelot.Chain.Middleware.Chain;
using Ocelot.Chain.Middleware.Step;
using Ocelot.Ipc.Lifestream;
using Ocelot.Ipc.VNavmesh;
using Ocelot.Services.Logger;
using Ocelot.Services.Pathfinding;

namespace BOCCHI.Common.Data.Aethernet;

public static class AethernetTeleport
{
    public static void AbortIfBusy(ILifestreamIpc lifestream)
    {
        if (lifestream.IsBusy())
        {
            lifestream.Abort();
        }
    }

    public static IChain BuildChain(
        IChain chain,
        IChainFactory chains,
        IZoneProvider zones,
        IObjectTable objects,
        IPathfinder pathfinder,
        IVNavmeshIpc vnav,
        ILifestreamIpc lifestream,
        ICondition conditions,
        MovementConfig movementConfig,
        ILogger logger,
        uint placeNameId)
    {
        string chainName = chain.Name;
        bool sprintEnabled = movementConfig.SprintOnAetheryteApproach;

        return chain
            .UseMiddleware<LogChainMiddleware>()
            .UseMiddleware(new RetryChainMiddleware(logger)
            {
                DelayMs = 500,
                MaxAttempts = 2, // Keep low — cancel mid-approach retried forever.
            })
            .UseStepMiddleware<LogStepMiddleware>()
            .UseStepMiddleware<RunOnMainThreadMiddleware>()
            .Then(_ =>
                {
                    IZone zone = zones.GetZone();
                    if (objects.LocalPlayer is { } player
                        && AetheryteApproach.IsAtPlaceName(zone, placeNameId, player.Position))
                    {
                        if (lifestream.IsBusy())
                        {
                            lifestream.Abort();
                        }

                        // Success (not Break): callers may AppendPath after this chain — Break would skip the walk.
                        logger.Debug("Already at aetheryte {Id} — skipping teleport", placeNameId);
                        return StepResult.Success();
                    }

                    return StepResult.Success();
                }, $"{chainName}::SkipIfAlreadyThere")
            .Then(AetheryteApproach.BuildApproachChain(
                chains,
                zones.GetZone(),
                objects,
                pathfinder,
                vnav,
                lifestream,
                conditions,
                movementConfig,
                $"{chainName}::Approach",
                sprintEnabled))
            .Then(_ =>
                {
                    if (objects.LocalPlayer is not { } player)
                    {
                        return StepResult.Failure("No local player.");
                    }

                    if (AetheryteApproach.IsAtPlaceName(zones.GetZone(), placeNameId, player.Position))
                    {
                        if (lifestream.IsBusy())
                        {
                            lifestream.Abort();
                        }

                        logger.Debug("Arrived at aetheryte {Id} during approach — skipping teleport", placeNameId);
                        return StepResult.Success();
                    }

                    if (!AetheryteApproach.IsReadyForLifestream(zones.GetZone(), lifestream, player.Position))
                    {
                        return StepResult.Failure("Not close enough to an aetheryte for Lifestream.");
                    }

                    return StepResult.Success();
                }, $"{chainName}::VerifyAetheryteRange")
            .Then(_ =>
                {
                    if (lifestream.IsBusy())
                    {
                        logger.Debug("Lifestream busy before teleport — aborting leftover task");
                        lifestream.Abort();
                    }

                    return StepResult.Success();
                }, $"{chainName}::AbortIfBusy")
            .WaitUntil(
                _ => ValueTask.FromResult(!lifestream.IsBusy()),
                TimeSpan.FromSeconds(3),
                TimeSpan.FromMilliseconds(250),
                $"{chainName}::WaitUntilLifestreamIsFree")
            .Then(_ =>
            {
                IZone zone = zones.GetZone();
                if (objects.LocalPlayer is { } player
                    && AetheryteApproach.IsAtPlaceName(zone, placeNameId, player.Position))
                {
                    if (lifestream.IsBusy())
                    {
                        lifestream.Abort();
                    }

                    logger.Debug("Already at destination aetheryte {Id} — skip Lifestream call", placeNameId);
                    return StepResult.Success();
                }

                if (!zone.IsUsableAethernetDestination(placeNameId))
                {
                    return StepResult.Failure($"Aethernet {placeNameId} is locked.");
                }

                if (!lifestream.AethernetTeleportByPlaceNameId(placeNameId))
                {
                    lifestream.Abort();
                    return StepResult.Failure("Lifestream rejected aethernet teleport.");
                }

                return StepResult.Success();
            }, $"{chainName}::Teleport")
            .WaitUntil(
                _ =>
                {
                    if (objects.LocalPlayer is { } player
                        && AetheryteApproach.IsAtPlaceName(zones.GetZone(), placeNameId, player.Position))
                    {
                        return ValueTask.FromResult(true);
                    }

                    return ValueTask.FromResult(lifestream.IsBusy());
                },
                TimeSpan.FromSeconds(3),
                TimeSpan.FromMilliseconds(200),
                $"{chainName}::WaitUntilTeleportStarted")
            .WaitUntil(
                _ =>
                {
                    if (objects.LocalPlayer is not { } player)
                    {
                        return ValueTask.FromResult(false);
                    }

                    if (!AetheryteApproach.IsAtPlaceName(zones.GetZone(), placeNameId, player.Position))
                    {
                        return ValueTask.FromResult(false);
                    }

                    if (lifestream.IsBusy())
                    {
                        lifestream.Abort();
                    }

                    return ValueTask.FromResult(true);
                },
                TimeSpan.FromSeconds(20),
                TimeSpan.FromMilliseconds(250),
                $"{chainName}::WaitUntilArrived")
            .Then(_ =>
                {
                    if (lifestream.IsBusy())
                    {
                        lifestream.Abort();
                    }

                    MountWait.ClearHardAndSoftTarget();
                    return StepResult.Success();
                }, $"{chainName}::ClearTargetAfterArrive")
            .Wait(TimeSpan.FromMilliseconds(500));
    }
}

public class AethernetTeleportChain
(
    IChainFactory chains,
    ILifestreamIpc lifestream,
    IZoneProvider zones,
    IObjectTable objects,
    IPathfinder pathfinder,
    IVNavmeshIpc vnav,
    ICondition conditions,
    MovementConfig movementConfig,
    ILogger<AethernetTeleportChain> logger
) : ChainRecipe<uint>(chains)
{
    public override string Name => "Aethernet Teleport";

    protected override IChain Compose(IChain chain, uint placeNameId) =>
        AethernetTeleport.BuildChain(
            chain,
            Chains,
            zones,
            objects,
            pathfinder,
            vnav,
            lifestream,
            conditions,
            movementConfig,
            logger,
            placeNameId);
}
