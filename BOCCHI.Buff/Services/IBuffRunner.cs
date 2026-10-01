using BOCCHI.Buff.Data;

namespace BOCCHI.Buff.Services;

public interface IBuffRunner
{
    bool IsRunning { get; }

    bool CanStart { get; }

    string? DisabledReason { get; }

    void Start();

    void StartWalkIn();

    void Stop();
}
