using Ocelot.Config.Renderers.Enum;
using Ocelot.Services.PluginStatus;

namespace BOCCHI.Common.Config;

public class CombatAutorotationFilter(IPluginStatus plugins, AutomatorConfig config)
    : IEnumFilter<CombatAutorotation>
{
    private const string BossModReborn = "BossModReborn";

    public bool Filter(CombatAutorotation value)
    {
        // Never hide the saved choice. The renderer falls back to the first entry when the current
        // value is absent, which would show someone a backend they did not pick as though they had.
        if (value == CombatAutorotation.None || value == config.CombatAutorotation)
        {
            return true;
        }

        return value switch
        {
            CombatAutorotation.WrathCombo => true,
            CombatAutorotation.BossMod => true,
            CombatAutorotation.RotationSolverReborn =>
                plugins.IsInstalled(CombatPluginPresence.RotationSolver),
            CombatAutorotation.BossModReborn => plugins.IsInstalled(BossModReborn),
            _ => false,
        };
    }
}
