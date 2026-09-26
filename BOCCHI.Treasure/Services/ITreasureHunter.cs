using BOCCHI.Treasure.Hunt;
using System.Numerics;

namespace BOCCHI.Treasure.Services;

public interface ITreasureHunter
{
    /// <summary>Hunt session is active (running or paused).</summary>
    bool Running { get; }

    bool Paused { get; }

    /// <summary>
    ///     Hunt is running but Update is idle because South Horn Ashkin / unsafe weather is active
    ///     and <c>SkipUnsafeTreasureWindows</c> is on.
    /// </summary>
    bool WaitingForSafeWindow { get; }

    /// <summary>
    ///     Hunt is running and either opening a coffer or standing next to the live, unopened
    ///     coffer of the current pad. Opportunistic yields (FATE / CE / pot) should wait briefly.
    /// </summary>
    bool IsFinishingCoffer { get; }

    int StepIndex { get; }

    int StepCount { get; }

    /// <summary>Coffers checked (opened / skipped) this session.</summary>
    int CheckedCofferCount { get; }

    /// <summary>Walk-to-coffer steps still in the current plan.</summary>
    int RemainingCofferCount { get; }

    float StepDistance { get; }

    TimeSpan Elapsed { get; }

    /// <summary>Layout node ID of the last coffer step that was completed this session.</summary>
    uint? LastCheckedNodeId { get; }

    /// <summary>Layout node IDs checked by the last completed Pots &amp; Treasure hunt run.</summary>
    IReadOnlySet<uint> LastCompletedRunNodeIds { get; }

    /// <summary>True while Pots &amp; Treasure owns this hunt session (hide standalone Start Hunt).</summary>
    bool ManagedByPotsTreasure { get; set; }

    /// <summary>True while Illegal Mode auto-filler owns this hunt session.</summary>
    bool ManagedByIllegalModeFiller { get; set; }

    /// <summary>True while Mob Farmer auto-yield owns this hunt session.</summary>
    bool ManagedByMobFarmer { get; set; }

    bool IsVnavAvailable { get; }

    bool IsVnavReady { get; }

    void Toggle();

    /// <summary>
    ///     Start a hunt owned by Pots &amp; Treasure or Illegal Mode filler.
    ///     Skips mode exclusivity so the parent mode is not torn down.
    /// </summary>
    void StartManaged();

    void ConfigureManagedRun(IReadOnlySet<uint> excludedNodeIds, int? maxLevelOverride = null);

    bool RecalculateRoute();

    void Pause();

    void Resume();

    /// <summary>
    ///     Resume and, when the next planned pad is far from the player, rebuild the route from
    ///     here (nearest remaining). Used by Illegal Mode map-hunt filler after a distant FATE/CE.
    /// </summary>
    void ResumeNearPlayer();

    HuntPathfinderStep? GetCurrentStep();

    /// <summary>Next WalkToNode coffer from the current step (where the hunt continues).</summary>
    bool TryGetResumeCoffer(out uint nodeId, out Vector3 position);

    /// <summary>Place the in-game map flag on the resume coffer position.</summary>
    bool FlagResumePoint();
}
