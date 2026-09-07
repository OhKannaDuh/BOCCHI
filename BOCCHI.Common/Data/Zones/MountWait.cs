using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using Ocelot.Actions;
using Ocelot.Extensions;
using System.Numerics;

namespace BOCCHI.Common.Data.Zones;

/// <summary>
///     Shared mount helpers: cast while pathing (mount is usable on the move).
/// </summary>
public static class MountWait
{
    private static DateTime lastTryCastUtc = DateTime.MinValue;

    private static readonly TimeSpan TryCastInterval = TimeSpan.FromMilliseconds(750);

    /// <summary>
    ///     Skip auto-mount near camp only when the route still ends by the crystal.
    /// </summary>
    private static bool ShouldSuppressMountNearCamp(IZone zone, Vector3 destination) =>
        destination.Distance2D(zone.GetAetherytePosition())
        <= NavigationConstants.CampRadius + NavigationConstants.MountMinDistance;

    private static bool ShouldSkip(
        ICondition conditions,
        IObjectTable objects,
        Vector3 destination,
        bool autoMountEnabled = true,
        bool inBaseCamp = false,
        IZone? zone = null)
    {
        bool suppressForCamp = inBaseCamp
                               && (zone == null || ShouldSuppressMountNearCamp(zone, destination));
        if (!autoMountEnabled || suppressForCamp)
        {
            return true;
        }

        if (conditions[ConditionFlag.Mounted]
            || conditions[ConditionFlag.Mounting]
            || conditions[ConditionFlag.Mounting71]
            || conditions[ConditionFlag.MountOrOrnamentTransition])
        {
            return true;
        }

        if (conditions[ConditionFlag.InCombat] || conditions[ConditionFlag.Unconscious])
        {
            return true;
        }

        // Right after aethernet / zone change the aetheryte is often still targeted and
        // the client rejects Mount with "Invalid target."
        if (conditions[ConditionFlag.BetweenAreas]
            || conditions[ConditionFlag.BetweenAreas51]
            || conditions[ConditionFlag.OccupiedInCutSceneEvent]
            || conditions[ConditionFlag.Casting])
        {
            return true;
        }

        if (objects.LocalPlayer is not { } player)
        {
            return true;
        }

        return player.Position.Distance(destination) <= NavigationConstants.MountMinDistance;
    }

    /// <summary>Drop hard/soft target (e.g. aetheryte after Lifestream so Mount is not “Invalid target”).</summary>
    public static unsafe void ClearHardAndSoftTarget()
    {
        TargetSystem* targets = TargetSystem.Instance();
        if (targets == null)
        {
            return;
        }

        targets->Target = null;
        targets->SoftTarget = null;
    }

    /// <param name="preferredMountId">Mount sheet row ID; 0 = Mount Roulette.</param>
    public static void TryCast(uint preferredMountId = 0)
    {
        ClearHardAndSoftTarget();

        Ocelot.Actions.Action mount = Actions.Mount(preferredMountId);
        if (mount.CanCast())
        {
            mount.Cast();
            return;
        }

        // Preferred mount unavailable (locked / on cooldown) → fall back to roulette.
        if (preferredMountId != 0 && Actions.MountRoulette.CanCast())
        {
            Actions.MountRoulette.Cast();
        }
    }

    public static void TryCastIfNeeded(
        ICondition conditions,
        IObjectTable objects,
        Vector3 destination,
        bool autoMountEnabled = true,
        uint preferredMountId = 0,
        bool inBaseCamp = false,
        IZone? zone = null)
    {
        if (ShouldSkip(conditions, objects, destination, autoMountEnabled, inBaseCamp, zone))
        {
            return;
        }

        if (DateTime.UtcNow - lastTryCastUtc < TryCastInterval)
        {
            return;
        }

        lastTryCastUtc = DateTime.UtcNow;
        TryCast(preferredMountId);
    }
}
