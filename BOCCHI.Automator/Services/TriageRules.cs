namespace BOCCHI.Automator.Services;

public enum TriageStage
{
    Waiting,
    Raising,
    TimedOut,
}

public static class TriageRules
{
    public static TriageStage Stage(TimeSpan elapsed, TimeSpan delay, TimeSpan timeout)
    {
        if (elapsed >= timeout + delay)
        {
            return TriageStage.TimedOut;
        }

        return elapsed < delay ? TriageStage.Waiting : TriageStage.Raising;
    }
}
