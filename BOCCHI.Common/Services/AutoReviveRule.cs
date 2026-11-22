namespace BOCCHI.Common.Services;

public enum AutoReviveAction
{
    Nothing,
    AcceptRaise,
    ReturnToCamp,
}

public readonly record struct AutoReviveState(
    bool AcceptRaises,
    bool ReturnToCamp,
    int ReturnDelaySeconds,
    bool PromptOpen,
    bool RaisePending,
    bool PromptIsReturn,
    bool InForkedTower,
    bool ProtectedContent,
    bool SawAlive,
    TimeSpan DeadFor);

public static class AutoReviveRule
{
    public static (AutoReviveAction Action, string Note) Decide(AutoReviveState state)
    {
        if (!state.PromptOpen)
        {
            return (AutoReviveAction.Nothing, "you are down; no revive prompt yet");
        }

        if (state.InForkedTower)
        {
            return (AutoReviveAction.Nothing, "inside Forked Tower, so not reviving");
        }

        if (state.RaisePending && !state.PromptIsReturn)
        {
            return state.AcceptRaises
                ? (AutoReviveAction.AcceptRaise, "accepting raise")
                : (AutoReviveAction.Nothing, "a raise is offered, but Accept raises is off");
        }

        string prompt = state.RaisePending
            ? "a raise is pending but the prompt looks like a return prompt"
            : "no raise pending";
        if (!state.ReturnToCamp)
        {
            return (AutoReviveAction.Nothing, $"{prompt}; Return to camp is off");
        }

        if (state.ProtectedContent)
        {
            return (AutoReviveAction.Nothing, $"{prompt}; your critical encounter is still running, so not returning");
        }

        if (!state.SawAlive)
        {
            return (AutoReviveAction.Nothing, $"{prompt}; BOCCHI did not see you alive first, so not returning");
        }

        if (state.DeadFor < TimeSpan.FromSeconds(state.ReturnDelaySeconds))
        {
            return (AutoReviveAction.Nothing, $"{prompt}; returning to camp after {state.ReturnDelaySeconds}s without a raise");
        }

        return (AutoReviveAction.ReturnToCamp, $"no raise after {state.ReturnDelaySeconds}s — returning to base camp");
    }
}
