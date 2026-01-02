using Ocelot.Chain;

namespace BOCCHI.Common.Services;

public interface IRepairService
{
    bool ShouldRepair();

    IChain Repair();
}