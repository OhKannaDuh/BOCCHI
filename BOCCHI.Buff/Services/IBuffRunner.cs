using BOCCHI.Buff.Data;

namespace BOCCHI.Buff.Services;

public interface IBuffRunner
{
    bool IsRunning { get; }

    bool CanStart { get; }

    string? DisabledReason { get; }

    void Start();

    /// <summary>Like <see cref="Start"/>, but walks into the buff circle from a nearby crystal.</summary>
    void StartWalkIn();

    void Stop();
}
