using BOCCHI.Common.Data.Paths;
using Ocelot.Chain;

namespace BOCCHI.Common.Services.Paths;

public interface IPathStepExecutor
{
    Task<ChainResult> Execute(IPathStep step);
}
