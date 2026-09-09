using BOCCHI.Common.Data.Zones;
using Dalamud.Plugin.Services;
using Ocelot.Chain;
using Ocelot.Ipc.VNavmesh;
using Ocelot.Services.Pathfinding;
using System.Numerics;

namespace BOCCHI.Common.Data.Aethernet;

/// <summary>
///     Shared Return-to-camp tick: walk toward camp while combat/unconscious blocks Return,
///     otherwise run <see cref="ReturnToBaseCamp"/>. Used by Treasure Hunt, Carrot Hunt, and shopping.
/// </summary>
public sealed class CampReturnSession(string chainPrefix)
{
    public enum TickResult
    {
        InProgress,
        Arrived,
        Failed,
    }

    private Task<ChainResult>? chain;

    public bool HasChain => chain is not null;

    public void Cancel(IChainManager chainManager, IPathfinder pathfinder, IVNavmeshIpc vnav)
    {
        if (chain is null)
        {
            return;
        }

        chainManager.CancelWhere(name => name.StartsWith(chainPrefix, StringComparison.Ordinal));
        chain = null;
        pathfinder.Stop();
        vnav.Stop();
    }

    /// <summary>Drop the tracked chain without cancelling (caller already cancelled via prefix).</summary>
    public void Detach() => chain = null;

    /// <param name="waitForPathfindIdleOnArrive">
    ///     Treasure Hunt: wait until Return's last pathfind slot is free before the next camp pad.
    /// </param>
    /// <param name="onCombatWalk">Optional side effect while walking toward camp (e.g. sprint).</param>
    public TickResult Tick(
        IZone zone,
        Vector3 playerPosition,
        bool blockedFromReturn,
        IZoneProvider zones,
        ICondition conditions,
        IGameGui gui,
        IPathfinder pathfinder,
        IVNavmeshIpc vnav,
        IChainManager chainManager,
        IChainFactory chains,
        bool waitForPathfindIdleOnArrive = false,
        Action? onCombatWalk = null)
    {
        if (zone.IsInBasecamp() && chain is null)
        {
            return waitForPathfindIdleOnArrive && vnav.IsPathfinding()
                ? TickResult.InProgress
                : TickResult.Arrived;
        }

        if (blockedFromReturn)
        {
            if (chain is not null)
            {
                chainManager.CancelWhere(name => name.StartsWith(chainPrefix, StringComparison.Ordinal));
                chain = null;
            }

            onCombatWalk?.Invoke();
            EnsureWalkTowardCamp(zone, playerPosition, vnav);
            return TickResult.InProgress;
        }

        if (vnav.IsRunning())
        {
            vnav.Stop();
        }

        if (chain is not null)
        {
            if (!chain.IsCompleted)
            {
                return TickResult.InProgress;
            }

            bool ok = chain.IsCompletedSuccessfully
                      && (chain.Result?.IsSuccess ?? false)
                      && zone.IsInBasecamp();
            chain = null;
            return ok ? TickResult.Arrived : TickResult.Failed;
        }

        if (zone.IsInBasecamp())
        {
            return waitForPathfindIdleOnArrive && vnav.IsPathfinding()
                ? TickResult.InProgress
                : TickResult.Arrived;
        }

        pathfinder.Stop();
        vnav.Stop();
        chain = chainManager.Manage(
            ReturnToBaseCamp.Append(
                chains.Create(chainPrefix),
                zones,
                conditions,
                gui,
                pathfinder,
                vnav));
        return TickResult.InProgress;
    }

    public static void EnsureWalkTowardCamp(IZone zone, Vector3 playerPosition, IVNavmeshIpc vnav)
    {
        Vector3 standOff = zone.GetMainAetheryte().GetCampStandOffPosition(playerPosition);
        if (!vnav.IsRunning() && !vnav.IsPathfinding())
        {
            vnav.PathfindAndMoveCloseTo(standOff, false, AethernetNavigation.PathfindArrivalRadius);
        }
    }
}
