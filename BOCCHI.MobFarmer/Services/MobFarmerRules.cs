namespace BOCCHI.MobFarmer.Services;

public static class MobFarmerRules
{
    public static bool ShouldStartPull(int freeEnemies, int minimum, TimeSpan waited, TimeSpan timeout) =>
        freeEnemies > 0 && (freeEnemies >= minimum || waited >= timeout);
}

// Object ids are reused by the game, so a skip must expire or a fresh enemy gets ignored.
public sealed class SkipList(TimeSpan duration)
{
    private readonly Dictionary<ulong, DateTime> until = [];

    public void Skip(ulong id, DateTime now) => until[id] = now + duration;

    public bool IsSkipped(ulong id, DateTime now) => until.TryGetValue(id, out DateTime end) && now < end;
}
