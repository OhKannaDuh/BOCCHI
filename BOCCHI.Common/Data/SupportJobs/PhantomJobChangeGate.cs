using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;

namespace BOCCHI.Common.Data.SupportJobs;

public static class PhantomJobChangeGate
{
    private static readonly TimeSpan PostCombatSettle = TimeSpan.FromSeconds(5);

    private static bool wasInCombat;

    private static DateTimeOffset combatClearedUtc = DateTimeOffset.MinValue;

    // The gate only sees combat end if it is asked during combat; callers that wait out a fight elsewhere report it here.
    public static void NoteCombatEnded() => combatClearedUtc = DateTimeOffset.UtcNow;

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
