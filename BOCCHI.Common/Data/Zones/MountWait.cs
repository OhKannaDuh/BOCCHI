using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using Ocelot.Actions;
using Ocelot.Extensions;
using System.Numerics;

namespace BOCCHI.Common.Data.Zones;

public static class MountWait
{
    private static DateTime lastTryCastUtc = DateTime.MinValue;

    private static readonly TimeSpan TryCastInterval = TimeSpan.FromMilliseconds(750);

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

    public static void TryCast(uint preferredMountId = 0)
    {
        ClearHardAndSoftTarget();

        Ocelot.Actions.Action mount = Actions.Mount(preferredMountId);
        if (mount.CanCast())
        {
            mount.Cast();
            return;
        }

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
