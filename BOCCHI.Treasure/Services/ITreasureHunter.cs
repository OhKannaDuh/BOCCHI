using BOCCHI.Treasure.Hunt;
using System.Numerics;

namespace BOCCHI.Treasure.Services;

public interface ITreasureHunter
{
    bool Running { get; }

    bool Paused { get; }

    bool WaitingForSafeWindow { get; }

    int StepIndex { get; }

    int StepCount { get; }

    int CheckedCofferCount { get; }

    int RemainingCofferCount { get; }

    float StepDistance { get; }

    TimeSpan Elapsed { get; }

    uint? LastCheckedNodeId { get; }

    IReadOnlySet<uint> LastCompletedRunNodeIds { get; }

    bool ManagedByPotsTreasure { get; set; }

    bool ManagedByIllegalModeFiller { get; set; }

    bool ManagedByMobFarmer { get; set; }

    bool IsVnavAvailable { get; }

    bool IsVnavReady { get; }

    void Toggle();

    void StartManaged();

    void ConfigureManagedRun(IReadOnlySet<uint> excludedNodeIds, int? maxLevelOverride = null);

    bool RecalculateRoute();

    void Pause();

    void Resume();

    void ResumeNearPlayer();

    HuntPathfinderStep? GetCurrentStep();

    bool TryGetResumeCoffer(out uint nodeId, out Vector3 position);

    bool FlagResumePoint();
}
