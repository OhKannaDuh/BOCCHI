using BOCCHI.Common.Config;

namespace BOCCHI.Automator.Services;

public static class ReturnDelay
{
    public static TimeSpan Roll(AutomatorConfig config)
    {
        int maxSeconds = Math.Max(2, config.MaxRemoteIdleTimeSeconds);
        return TimeSpan.FromSeconds(Random.Shared.Next(2, maxSeconds + 1));
    }
}

public static class BaseTeleportDelay
{
    public static TimeSpan Roll(AutomatorConfig config)
    {
        int maxSeconds = Math.Max(0, config.MaxBaseTeleportDelaySeconds);
        if (maxSeconds == 0)
        {
            return TimeSpan.Zero;
        }

        return TimeSpan.FromSeconds(Random.Shared.Next(0, maxSeconds + 1));
    }
}
