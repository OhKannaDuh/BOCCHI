using Ocelot.Ipc.RotationSolverReborn;
using Ocelot.Services.PluginStatus;

namespace BOCCHI.Common.Config;

public static class CombatPluginPresence
{
    public const string RotationSolver = "RotationSolver";

    public static bool RotationSolverReborn(IPluginStatus plugins, IRotationSolverRebornIpc rsr) =>
        plugins.IsLoaded(RotationSolver) || rsr.IsAvailable;
}
