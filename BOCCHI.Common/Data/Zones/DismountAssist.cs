using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using ECommons.GameHelpers;
using ECommons.Throttlers;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using Ocelot.Actions;

namespace BOCCHI.Common.Data.Zones;

public static class DismountAssist
{
    public static unsafe bool IsMounted(ICondition conditions)
    {
        if (conditions[ConditionFlag.Mounted] || conditions[ConditionFlag.RidingPillion])
        {
            return true;
        }

        return Player.Object is { Address: var address } && address != nint.Zero
               && ((BattleChara*)address)->IsMounted();
    }

    private static bool IsMountTransition(ICondition conditions) =>
        conditions[ConditionFlag.Mounting]
        || conditions[ConditionFlag.Mounting71]
        || conditions[ConditionFlag.MountOrOrnamentTransition];

    public static bool TryDismount(ICondition conditions) => TryDismount(conditions, null);

    public static bool TryDismount(ICondition conditions, System.Action<string>? report)
    {
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
