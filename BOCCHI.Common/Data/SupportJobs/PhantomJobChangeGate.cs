using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;

namespace BOCCHI.Common.Data.SupportJobs;

public static class PhantomJobChangeGate
{
    private static readonly TimeSpan PostCombatSettle = TimeSpan.FromSeconds(4);

    private static bool wasInCombat;

    private static DateTimeOffset combatClearedUtc = DateTimeOffset.MinValue;

    public static bool IsBlocked(ICondition conditions)
    {
        bool inCombat = conditions[ConditionFlag.InCombat];
        if (wasInCombat && !inCombat)
        {
            combatClearedUtc = DateTimeOffset.UtcNow;
        }

        wasInCombat = inCombat;

        if (inCombat
            || DateTimeOffset.UtcNow - combatClearedUtc < PostCombatSettle
            || conditions[ConditionFlag.BetweenAreas]
            || conditions[ConditionFlag.BetweenAreas51]
            || conditions[ConditionFlag.Casting]
            || conditions[ConditionFlag.Casting87]
            || conditions[ConditionFlag.Jumping]
            || conditions[ConditionFlag.Jumping61]
            || conditions[ConditionFlag.Occupied]
            || conditions[ConditionFlag.OccupiedInEvent]
            || conditions[ConditionFlag.OccupiedInQuestEvent]
            || conditions[ConditionFlag.OccupiedInCutSceneEvent])
        {
            return true;
        }

        return false;
    }
}
