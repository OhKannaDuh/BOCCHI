using BOCCHI.Automator.Data;
using BOCCHI.Automator.Services.Goals;
using BOCCHI.Common.Config;
using BOCCHI.Common.Data.Fates;
using BOCCHI.Common.Data.Zones;
using BOCCHI.Common.Services;

namespace BOCCHI.Automator.Services;

/// <summary>
///     Whether Illegal Mode would pick a live CE or FATE right now (not pot preposition).
/// </summary>
public interface IIllegalModeStartableActivityProbe
{
    bool HasStartableFateOrCriticalEncounter();

    bool HasStartableCriticalEncounter();

    bool HasStartableFate();
}

public sealed class IllegalModeStartableActivityProbe(
    IStartableCriticalEncounterFinder startableCriticalEncounters,
    IFateRepository fateRepository,
    IFateScorer fateScorer,
    IPotCycleTracker potCycle,
    IZoneProvider zones,
    IAutomatorContext automatorContext,
    IFieldNoteTracker fieldNotes,
    AutomatorConfig automatorConfig,
    FatesConfig fatesConfig,
    PotsConfig potsConfig
) : IIllegalModeStartableActivityProbe
{
    public bool HasStartableFateOrCriticalEncounter() =>
        HasStartableCriticalEncounter() || HasStartableFate();

    public bool HasStartableCriticalEncounter() =>
        !automatorContext.IsPotsAndTreasure
        && startableCriticalEncounters.FindStartable() != null;

    public bool HasStartableFate() =>
        LivePotPriority.FindBest(
            fateRepository.Snapshot(),
            zones.GetZone(),
            fateScorer,
            potCycle,
            automatorConfig,
            fatesConfig,
            potsConfig,
            automatorContext,
            fieldNotes) != null;
}
