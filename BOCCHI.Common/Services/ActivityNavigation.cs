using BOCCHI.Common.Config;
using BOCCHI.Common.Data.Aethernet;
using BOCCHI.Common.Data.Zones;
using BOCCHI.Common.Data.Zones.Graph;
using BOCCHI.Common.Data.Zones.Graph.Traversal;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using Ocelot.Chain;
using Ocelot.Chain.Extensions;
using Ocelot.Chain.Recipes;
using Ocelot.Extensions;
using Ocelot.Ipc.Lifestream;
using Ocelot.Ipc.VNavmesh;
using Ocelot.Services.Logger;
using Ocelot.Services.Pathfinding;
using Ocelot.Services.PlayerState;
using System.Numerics;
using Path = Ocelot.Services.Pathfinding.Path;

namespace BOCCHI.Common.Services;

public class ActivityNavigation
(
    IChainFactory chains,
    IChainManager manager,
    IZoneProvider zones,
    IObjectTable objects,
    IPathfinder pathfinder,
    IVNavmeshIpc vnav,
    ILifestreamIpc lifestream,
    ICondition conditions,
    IGameGui gui,
    IPlayer player,
    IFramework framework,
    MovementConfig movementConfig,
    CriticalEncounterGeometry geometry,
    ILogger<ActivityNavigation> logger
) : IActivityNavigation
{
    private enum SurveyRoute
    {
        Direct,
        FieldAethernet,
        ReturnThenAethernet,
    }

    private const string ChainPrefix = "ActivityGoto::";

    private int navigationGeneration;

    public bool CanPathfind => vnav.IsNavmeshReady();

    public bool CanTeleport(Vector3 destination, out string? disabledReason)
    {
        _ = destination;
        if (!TryValidateOccultAethernet(out disabledReason))
        {
            return false;
        }

        if (!zones.GetZone().IsWithinLifestreamRange(player.Position))
        {
            disabledReason = "You must be near an aetheryte to teleport.";
            return false;
        }

        // Do not gate on "already at nearest" — Euclidean nearest can be an island
        // shard that cannot walk to the destination (e.g. Unhallowed Hamlet → Eye to Eye).
        disabledReason = null;
        return true;
    }

    public void PathTo(Vector3 destination, string name, string id) =>
        StartPath(destination, name, id, treatAsActivity: true);

    public void PathToPoint(Vector3 destination, string name, string id) =>
        StartPath(SeedDestinationAltitude(destination), name, id, treatAsActivity: false);

    public void TeleportToward(Vector3 destination, string name, string id)
    {
        if (!CanTeleport(destination, out string? reason))
        {
            logger.Warning("Cannot teleport toward {Name}: {Reason}", name, reason ?? "unknown");
            return;
        }

        int generation = BeginNavigation();
        _ = TeleportOnlyAsync(destination, name, id, generation);
    }

    private const float SurveySnapExtentXZ = 5f;

    private const float SurveySnapExtentY = 200f;

    private Vector3 SeedDestinationAltitude(Vector3 destination)
    {
        Vector3 seed = new(destination.X, player.Position.Y, destination.Z);

        Vector3 onMesh = vnav.FindPointOnMesh(seed, SurveySnapExtentXZ, SurveySnapExtentY);
        if (onMesh != seed)
        {
            Vector3 floored = vnav.FindPointOnFloor(onMesh, SurveySnapExtentXZ);
            return floored != onMesh ? floored : onMesh;
        }

        logger.Warning(
            "Survey point {Pos:F0}: no navmesh within {Extent:F0}y vertically — pathing with our own altitude",
            destination,
            SurveySnapExtentY);
        return seed;
    }

    private void StartPath(Vector3 destination, string name, string id, bool treatAsActivity)
    {
        if (!CanPathfind)
        {
            logger.Warning("Navmesh not ready — cannot path to {Name}", name);
            return;
        }

        if (!treatAsActivity)
        {
            int generation = BeginNavigation();
            _ = PathToSurveyAsync(destination, name, id, generation);
            return;
        }

        if (CanTeleport(destination, out _))
        {
            int generation = BeginNavigation();
            _ = PathViaAethernetAsync(destination, name, id, generation);
            return;
        }

        if (!TryResolveWalkTarget(destination, player.Position, treatAsActivity: true, out Vector3 approach, out bool alreadyAtCeRing))
        {
            return;
        }

        if (alreadyAtCeRing)
        {
            logger.Debug("Already inside CE wait area for {Name} — not pathing into the center", name);
            CancelActivityChains();
            pathfinder.Stop();
            return;
        }

        logger.Debug("Pathfinding to {Name} at {Destination:f1}", name, approach);
        CancelActivityChains();
        _ = manager.Manage(BuildPathChain($"{ChainPrefix}Path::{id}", () => approach, treatAsActivity: true));
    }

    private async Task PathToSurveyAsync(Vector3 destination, string name, string id, int generation)
    {
        try
        {
            Vector3 start = player.Position;
            IZone zone = zones.GetZone();
            bool inCamp = zone.IsInBasecamp();

            (AethernetData? best, float walkFromBest) = await SelectBestAetheryteWithDistanceAsync(destination, treatAsActivity: false)
                .ConfigureAwait(false);
            if (generation != navigationGeneration)
            {
                return;
            }

            float directScore = await MeasureWalkDistanceAsync(start, destination).ConfigureAwait(false);
            if (generation != navigationGeneration)
            {
                return;
            }

            float walkToLifestream = 0f;
            if (!AetheryteApproach.IsReadyForLifestream(zone, lifestream, start))
            {
                AethernetData? nearest = zone.GetAetherytes()
                    .OrderBy(a => start.Distance2D(a.Position))
                    .FirstOrDefault();
                walkToLifestream = nearest == null
                    ? float.PositiveInfinity
                    : await MeasureWalkDistanceAsync(start, nearest.GetCampStandOffPosition(start))
                        .ConfigureAwait(false);
            }

            if (generation != navigationGeneration)
            {
                return;
            }

            bool alreadyAtBest = best != null && AetheryteApproach.IsAlreadyAtAetheryte(best, start);
            bool bestIsMain = best != null && best.Id == zone.GetMainAetheryte().Id;

            float fieldScore = float.PositiveInfinity;
            if (best != null && !float.IsPositiveInfinity(walkFromBest))
            {
                fieldScore = alreadyAtBest
                    ? walkFromBest
                    : walkToLifestream + NavigationConstants.AethernetHopCost + walkFromBest;
            }

            float returnScore = float.PositiveInfinity;
            if (!inCamp
                && best != null
                && !float.IsPositiveInfinity(walkFromBest)
                && CanOfferSurveyReturn())
            {
                float teleportLeg = bestIsMain ? 0f : NavigationConstants.AethernetHopCost;
                returnScore = NavigationConstants.ReturnCost + teleportLeg + walkFromBest;
            }

            SurveyRoute route = SurveyRoute.Direct;
            float bestScore = directScore;
            if (fieldScore < bestScore)
            {
                bestScore = fieldScore;
                route = SurveyRoute.FieldAethernet;
            }

            if (returnScore < bestScore)
            {
                bestScore = returnScore;
                route = SurveyRoute.ReturnThenAethernet;
            }

            if (float.IsPositiveInfinity(bestScore))
            {
                route = TryValidateOccultAethernet(out _) ? SurveyRoute.FieldAethernet : SurveyRoute.Direct;
            }

            logger.Debug(
                "Survey route for {Name}: {Route} (direct={Direct:f0}, field={Field:f0}, return={Return:f0})",
                name,
                route,
                directScore,
                fieldScore,
                returnScore);

            await framework.Run(() =>
            {
                if (generation != navigationGeneration)
                {
                    return;
                }

                ExecuteSurveyRoute(route, destination, best, name, id);
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Failed survey path toward {Name}", name);
        }
    }

    private bool CanOfferSurveyReturn()
    {
        if (conditions[ConditionFlag.Unconscious] || conditions[ConditionFlag.InCombat])
        {
            return false;
        }

        return OccultReturn.CanCast()
               || conditions[ConditionFlag.Mounted]
               || conditions[ConditionFlag.Mounting];
    }

    private void ExecuteSurveyRoute(
        SurveyRoute route,
        Vector3 destination,
        AethernetData? target,
        string name,
        string id)
    {
        if (!TryResolveWalkTarget(destination, player.Position, treatAsActivity: false, out Vector3 approach, out _))
        {
            return;
        }

        Func<Vector3> walkTo = () => approach;
        string chainName = $"{ChainPrefix}Path::{id}";

        if (route == SurveyRoute.Direct)
        {
            logger.Debug("Pathfinding directly to survey {Name} at {Destination:f1}", name, approach);
            _ = manager.Manage(BuildPathChain(chainName, walkTo, treatAsActivity: false));
            return;
        }

        bool prependReturn = route == SurveyRoute.ReturnThenAethernet;
        IChain chain = chains.Create(chainName);
        if (prependReturn)
        {
            logger.Debug("Return to base camp, then path to survey {Name}", name);
            chain = ReturnToBaseCamp.Append(chain, zones, conditions, gui, pathfinder, vnav);
        }

        ManageHopThenWalk(
            chain,
            chainName,
            target,
            walkTo,
            name,
            treatAsActivity: false,
            checkAlreadyAtTarget: !prependReturn);
    }

    private async Task<float> MeasureWalkDistanceAsync(Vector3 from, Vector3 to)
    {
        if (from.Distance2D(to) <= 2f)
        {
            return 0f;
        }

        if (!vnav.IsNavmeshReady())
        {
            return from.Distance2D(to);
        }

        Path path = await pathfinder.Pathfind(new PathfinderConfig(to)
            {
                From = from,
                AllowFlying = false,
                ShouldSnapToFloor = true,
            })
            .ConfigureAwait(false);

        if (path.Nodes.Count < 2 || float.IsPositiveInfinity(path.Distance) || path.Distance <= 0f)
        {
            return float.PositiveInfinity;
        }

        return path.Distance;
    }

    private bool TryValidateOccultAethernet(out string? disabledReason)
    {
        IZone zone = zones.GetZone();
        if (!zone.IsOccultCrescentZone())
        {
            disabledReason = "Not in a supported Occult Crescent zone.";
            return false;
        }

        if (zone.GetAetherytes().Count == 0)
        {
            disabledReason = "No aethernet destination found.";
            return false;
        }

        disabledReason = null;
        return true;
    }

    private async Task PathViaAethernetAsync(Vector3 destination, string name, string id, int generation)
    {
        try
        {
            AethernetData? target = await SelectBestAetheryteAsync(destination, treatAsActivity: true)
                .ConfigureAwait(false);
            if (generation != navigationGeneration)
            {
                return;
            }

            await framework.Run(() =>
            {
                if (generation != navigationGeneration)
                {
                    return;
                }

                Vector3 approachFrom = target?.Position ?? player.Position;
                if (!TryResolveWalkTarget(
                        destination,
                        approachFrom,
                        treatAsActivity: true,
                        out Vector3 approach,
                        out bool alreadyAtCeRing))
                {
                    return;
                }

                if (alreadyAtCeRing)
                {
                    logger.Debug("Already inside CE wait area for {Name} — not pathing into the center", name);
                    pathfinder.Stop();
                    return;
                }

                ManageHopThenWalk(
                    chains.Create($"{ChainPrefix}Path::{id}"),
                    $"{ChainPrefix}Path::{id}",
                    target,
                    () => approach,
                    name,
                    treatAsActivity: true,
                    checkAlreadyAtTarget: true);
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Failed path via aethernet toward {Name}", name);
        }
    }

    private async Task TeleportOnlyAsync(Vector3 destination, string name, string id, int generation)
    {
        try
        {
            AethernetData? target = await SelectBestAetheryteAsync(destination, treatAsActivity: true)
                .ConfigureAwait(false);
            if (generation != navigationGeneration)
            {
                return;
            }

            await framework.Run(() =>
            {
                if (generation != navigationGeneration)
                {
                    return;
                }

                if (target == null)
                {
                    logger.Warning("No aethernet found for teleport toward {Name}", name);
                    return;
                }

                if (AetheryteApproach.IsAlreadyAtAetheryte(target, player.Position))
                {
                    logger.Debug(
                        "Already at best aethernet {Aethernet} for {Name} — teleport does not pathfind",
                        target.Id,
                        name);
                    return;
                }

                logger.Debug("Teleporting via {Aethernet} toward {Name}", target.Id, name);

                IChain chain = AethernetTeleport.BuildChain(
                    chains.Create($"{ChainPrefix}Teleport::{id}"),
                    chains,
                    zones,
                    objects,
                    pathfinder,
                    vnav,
                    lifestream,
                    conditions,
                    movementConfig,
                    logger,
                    target.Id);

                _ = manager.Manage(chain);
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Failed teleport toward {Name}", name);
        }
    }

    private void ManageHopThenWalk(
        IChain chain,
        string chainName,
        AethernetData? target,
        Func<Vector3> walkTo,
        string name,
        bool treatAsActivity,
        bool checkAlreadyAtTarget)
    {
        if (target == null
            || (checkAlreadyAtTarget && AetheryteApproach.IsAlreadyAtAetheryte(target, player.Position)))
        {
            if (target != null)
            {
                logger.Debug("Already at best aethernet — vnav to {Name}", name);
            }

            _ = manager.Manage(AppendPath(chain, chainName, walkTo, treatAsActivity));
            return;
        }

        logger.Debug("Lifestream to aethernet {Aethernet}, then vnav to {Name}", target.Id, name);

        chain = AethernetTeleport.BuildChain(
            chain,
            chains,
            zones,
            objects,
            pathfinder,
            vnav,
            lifestream,
            conditions,
            movementConfig,
            logger,
            target.Id);

        _ = manager.Manage(AppendPath(chain, chainName, walkTo, treatAsActivity));
    }

    private IChain BuildPathChain(string name, Func<Vector3> destination, bool treatAsActivity = true) =>
        AppendPath(chains.Create(name), name, destination, treatAsActivity);

    private bool TryResolveWalkTarget(
        Vector3 destination,
        Vector3 from,
        bool treatAsActivity,
        out Vector3 approach,
        out bool alreadyAtCeRing)
    {
        alreadyAtCeRing = false;
        if (!treatAsActivity)
        {
            approach = destination;
            return true;
        }

        IZone zone = zones.GetZone();
        if (NavigationApproach.TryResolveCriticalEncounterApproach(
                zone, geometry, destination, from, out approach, out _, out alreadyAtCeRing))
        {
            return true;
        }

        approach = NavigationApproach.GetEventPosition(destination, from);
        return true;
    }

    private IChain AppendPath(IChain chain, string name, Func<Vector3> destination, bool treatAsActivity = true) =>
        chain.Then<PathfindToChain, PathfinderConfig>(new(destination)
        {
            DistanceThreshold = 2f,
            ShouldSnapToFloor = true,
            FloorSnapExtents = 40f,
            WhileMoving = () =>
            {
                Vector3 dest = destination();
                IZone zone = zones.GetZone();
                MountWait.TryCastIfNeeded(
                    conditions,
                    objects,
                    dest,
                    movementConfig.ShouldAutoMount,
                    movementConfig.PreferredMountId,
                    treatAsActivity && zone.IsInBasecamp(),
                    zone);
            },
        });

    private async Task<AethernetData?> SelectBestAetheryteAsync(Vector3 destination, bool treatAsActivity)
    {
        (AethernetData? best, _) = await SelectBestAetheryteWithDistanceAsync(destination, treatAsActivity)
            .ConfigureAwait(false);
        return best;
    }

    private async Task<(AethernetData? Aetheryte, float WalkDistance)> SelectBestAetheryteWithDistanceAsync(
        Vector3 destination,
        bool treatAsActivity)
    {
        List<AethernetData> aetherytes = zones.GetZone().EnumerateUsableAetherytes().ToList();
        if (aetherytes.Count == 0)
        {
            return (null, float.PositiveInfinity);
        }

        uint? preferredId = treatAsActivity ? FindPreferredAethernetId(destination) : null;

        AethernetData? ByEuclidean() => aetherytes
            .OrderBy(a => preferredId is { } pid && a.Id == pid ? 0 : 1)
            .ThenBy(a => destination.Distance2D(a.Position))
            .FirstOrDefault();

        if (!vnav.IsNavmeshReady())
        {
            AethernetData? euclidean = ByEuclidean();
            float estimate = euclidean == null
                ? float.PositiveInfinity
                : destination.Distance2D(euclidean.Position);
            return (euclidean, estimate);
        }

        List<AethernetData> candidates = [];
        if (preferredId is { } preferred)
        {
            AethernetData? preferredShard = aetherytes.FirstOrDefault(a => a.Id == preferred);
            if (preferredShard != null)
            {
                candidates.Add(preferredShard);
            }
        }

        foreach (AethernetData aetheryte in aetherytes.OrderBy(a => destination.Distance2D(a.Position)))
        {
            if (candidates.Count >= 4)
            {
                break;
            }

            if (candidates.Any(c => c.Id == aetheryte.Id))
            {
                continue;
            }

            candidates.Add(aetheryte);
        }

        AethernetData? best = null;
        float bestDistance = float.PositiveInfinity;

        foreach (AethernetData aetheryte in candidates)
        {
            Vector3 from = aetheryte.GetInteractPosition();
            Path path = await pathfinder.Pathfind(new PathfinderConfig(destination)
                {
                    From = from,
                    AllowFlying = false,
                    ShouldSnapToFloor = true
                })
                .ConfigureAwait(false);

            if (path.Nodes.Count < 2 || float.IsPositiveInfinity(path.Distance) || path.Distance <= 0f)
            {
                continue;
            }

            if (preferredId is { } pid && aetheryte.Id == pid)
            {
                return (aetheryte, path.Distance);
            }

            if (path.Distance < bestDistance)
            {
                bestDistance = path.Distance;
                best = aetheryte;
            }
        }

        if (best != null)
        {
            return (best, bestDistance);
        }

        AethernetData? fallback = ByEuclidean();
        float fallbackDistance = fallback == null
            ? float.PositiveInfinity
            : destination.Distance2D(fallback.Position);
        return (fallback, fallbackDistance);
    }

    private uint? FindPreferredAethernetId(Vector3 destination)
    {
        IZone zone = zones.GetZone();
        const float matchRadius = 80f;
        foreach (ActivityData activity in zone.GetNormalFateData()
                     .Concat(zone.GetPotFateData())
                     .Concat(zone.GetCriticalEncounterData()))
        {
            if (activity.PreferredAethernetId is not { } preferred)
            {
                continue;
            }

            if (destination.Distance2D(activity.Position) <= matchRadius)
            {
                return preferred;
            }
        }

        return null;
    }

    private int BeginNavigation()
    {
        int generation = Interlocked.Increment(ref navigationGeneration);
        manager.CancelWhere(name => name.StartsWith(ChainPrefix, StringComparison.Ordinal));
        AethernetTeleport.AbortIfBusy(lifestream);
        return generation;
    }

    private void CancelActivityChains()
    {
        Interlocked.Increment(ref navigationGeneration);
        manager.CancelWhere(name => name.StartsWith(ChainPrefix, StringComparison.Ordinal));
        AethernetTeleport.AbortIfBusy(lifestream);
    }
}
