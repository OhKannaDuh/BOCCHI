namespace BOCCHI.Common.Services;

public static class CombatRules
{
    public static readonly TimeSpan OutrunGrace = TimeSpan.FromSeconds(10);

    public static bool CanFight(int? playerLevel, int? highestAttackerLevel) =>
        playerLevel is not { } mine || highestAttackerLevel is not { } enemy || enemy <= mine;
}

public sealed class OutrunTimer(TimeSpan grace)
{
    private DateTimeOffset? combatSince;

    public OutrunTimer() : this(CombatRules.OutrunGrace)
    {
    }

    public bool Update(bool inCombat, DateTimeOffset now)
    {
        combatSince = inCombat ? combatSince ?? now : null;
        return combatSince is { } since && now - since >= grace;
    }

    public void Reset() => combatSince = null;
}
