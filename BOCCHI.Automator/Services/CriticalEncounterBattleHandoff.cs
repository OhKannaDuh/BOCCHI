using System.Numerics;
using BOCCHI.Common.Data.CriticalEncounters;
using BOCCHI.Common.Data.Zones;
using BOCCHI.Common.Services;

namespace BOCCHI.Automator.Services;

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
