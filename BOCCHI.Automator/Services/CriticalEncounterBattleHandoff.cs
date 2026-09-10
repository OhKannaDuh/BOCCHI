using System.Numerics;
using BOCCHI.Common.Data.CriticalEncounters;
using BOCCHI.Common.Data.Zones;
using BOCCHI.Common.Services;

namespace BOCCHI.Automator.Services;

/// <summary>
///     CE Preparing→Battle: still in the registration area (or EventId / CE-tagged enemies).
///     Open-world InCombat alone is not enough (#196).
/// </summary>
internal static class CriticalEncounterBattleHandoff
{
    public static bool IsReady(
        CriticalEncounter encounter,
        ICriticalEncounterContext context,
        Vector3 playerPosition)
    {
        if (context.GetCriticalEncounterId() == encounter.Id
            || context.HasEncounterEnemies(encounter.Id))
        {
            return true;
        }

        // Already waiting for this CE, battle started, still inside the registration edge —
        // EventId / enemy tags can lag for seconds after Warmup→Battle.
        if (!encounter.IsActive())
        {
            return false;
        }

        float combatRadius = NavigationConstants.CriticalEncounterRedRadius(
            encounter.Radius,
            encounter.AreaShape);
        return NavigationConstants.IsInsideCriticalEncounterRegistrationArea(
            encounter.RegistrationCenter,
            combatRadius,
            encounter.AreaShape,
            playerPosition);
    }
}
