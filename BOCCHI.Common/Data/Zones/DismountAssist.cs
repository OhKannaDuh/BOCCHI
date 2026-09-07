using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using ECommons.GameHelpers;
using ECommons.Throttlers;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using Ocelot.Actions;

namespace BOCCHI.Common.Data.Zones;

/// <summary>Shared dismount helper for state handlers.</summary>
public static class DismountAssist
{
    /// <summary>
    ///     ConditionFlag.Mounted lags the character by a frame or two, so a flag-only test can
    ///     report "on foot" while still mounted. Callers then skip the dismount and the interact
    ///     behind it fails silently — no dismount, no open (#175). Ask the character itself as well
    ///     and treat any positive signal as mounted: a redundant dismount cast is a no-op, a missed
    ///     one costs the chest.
    /// </summary>
    public static unsafe bool IsMounted(ICondition conditions)
    {
        if (conditions[ConditionFlag.Mounted] || conditions[ConditionFlag.RidingPillion])
        {
            return true;
        }

        return Player.Object is { Address: var address } && address != nint.Zero
               && ((BattleChara*)address)->IsMounted();
    }

    /// <summary>
    ///     Mid mount / dismount animation (MountOrOrnamentTransition, Mounting, Mounting71).
    /// </summary>
    private static bool IsMountTransition(ICondition conditions) =>
        conditions[ConditionFlag.Mounting]
        || conditions[ConditionFlag.Mounting71]
        || conditions[ConditionFlag.MountOrOrnamentTransition];

    /// <summary>
    ///     If mounted, mounting, or still in the dismount jump/landing, try to dismount / wait.
    ///     Returns true when the caller should wait (not act yet).
    /// </summary>
    public static bool TryDismount(ICondition conditions) => TryDismount(conditions, null);

    /// <param name="report">
    ///     Optional sink for one line per cast attempt. This path has failed silently more than once,
    ///     so callers that care can see the flags and the UseAction result instead of guessing.
    /// </param>
    public static bool TryDismount(ICondition conditions, System.Action<string>? report)
    {
        // Wait out mount-up / dismount animation without casting Dismount into a mount-in-progress.
        if (IsMountTransition(conditions))
        {
            return true;
        }

        bool mounted = IsMounted(conditions);

        if (!mounted)
        {
            // On foot already. Dismounting leaves a jump/landing beat and actions fail with
            // "while jumping", so that is the only thing left to wait out.
            return Player.IsJumping;
        }

        // Cast while mounted regardless of IsJumping: the dismount hop counts as jumping, and
        // bailing here meant a character that never touched down never dismounted at all.
        // No CanCast() gate — GetActionStatus reports non-zero for this general action, and the
        // paths that actually work (UnmountStep) only check IsMounted.
        if (EzThrottler.Throttle("DismountAssist::Dismount", 250))
        {
            bool sent = Actions.Dismount.Cast();
            report?.Invoke(
                $"sent={sent} flags={conditions[ConditionFlag.Mounted]}/{conditions[ConditionFlag.RidingPillion]}"
                + $" character={mounted} jumping={Player.IsJumping}");
        }

        return true;
    }
}
