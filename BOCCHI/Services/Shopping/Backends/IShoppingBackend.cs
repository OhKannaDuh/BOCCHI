using BOCCHI.Common.Config;

namespace BOCCHI.Services.Shopping.Backends;

public enum ShoppingReadiness
{
    Ready,

    /// <summary>The plugin is doing something else — try again shortly, no cooldown.</summary>
    Busy,

    /// <summary>Missing plugin / list / nothing to buy — back off instead of Returning to camp.</summary>
    NotReady,
}

public enum ShoppingStartKind
{
    Started,

    /// <summary>Transient (busy, data still loading) — keep the session and retry next tick.</summary>
    Retry,

    /// <summary>Will not start without player action — end the session and cool down.</summary>
    Rejected,
}

public readonly record struct ShoppingStart(ShoppingStartKind Kind, string Message);

public readonly record struct ShoppingPoll(bool IsFinished, bool InsufficientFunds, string Message);

/// <summary>
///     A plugin that buys the Occult Crescent list once the player is at base camp.
///     Implementations own at most one run at a time.
/// </summary>
public interface IShoppingBackend
{
    ShoppingBackendKind Kind { get; }

    string Name { get; }

    bool IsAvailable { get; }

    /// <summary>A run started through this backend has not finished yet.</summary>
    bool HasRun { get; }

    /// <summary>Checked before Returning to camp so a pointless trip is skipped.</summary>
    ShoppingReadiness CheckReady(out string reason);

    ShoppingStart TryStart();

    /// <summary>Only meaningful while <see cref="HasRun"/>; a finished poll clears the run.</summary>
    ShoppingPoll Poll();

    /// <summary>Stop the in-flight run (if any) and forget it.</summary>
    void Cancel();

    /// <summary>Drop run bookkeeping without touching the plugin (it unloaded or already stopped).</summary>
    void Forget();

    string DescribeStatus();
}
