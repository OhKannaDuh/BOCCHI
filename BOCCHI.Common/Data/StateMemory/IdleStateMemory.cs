namespace BOCCHI.Automator.Data.StateMemory;

public sealed class IdleStateMemory
{
    public DateTimeOffset Entered { get; } = DateTimeOffset.UtcNow;

    public TimeSpan GetIdleTime()
    {
        return DateTimeOffset.UtcNow - Entered;
    }
}
