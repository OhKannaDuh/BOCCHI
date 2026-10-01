using Ocelot.Actions;

namespace BOCCHI.Common.Data.Zones;

public static class SprintAssist
{
    public static void MaybeCast(bool enabled = true, bool inBasecamp = false)
    {
        if (!enabled || inBasecamp || !Actions.Sprint.CanCast())
        {
            return;
        }

        using (ActionCastScope.SuppressPathfindCancel())
        {
            Actions.Sprint.Cast();
        }
    }
}
