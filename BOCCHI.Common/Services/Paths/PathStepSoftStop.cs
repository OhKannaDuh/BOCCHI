using Ocelot.Chain;
using Ocelot.Ipc.VNavmesh;
using Ocelot.Services.Pathfinding;

namespace BOCCHI.Common.Services.Paths;

public static class PathStepSoftStop
{
    public const string Prefix = "PathStep::";

    public static bool IsPathStepChain(string name) =>
        name.StartsWith(Prefix, StringComparison.Ordinal);

    public static void Cancel(IChainManager chains) =>
        chains.CancelWhere(IsPathStepChain);

    public static void Stop(IChainManager chains, IPathfinder pathfinder, IVNavmeshIpc? vnav = null)
    {
        Cancel(chains);
        pathfinder.Stop();
        vnav?.Stop();
    }
}
