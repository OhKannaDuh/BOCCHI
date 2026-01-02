using BOCCHI.Common.Data.Goals;
using BOCCHI.Common.Data.Paths;

namespace BOCCHI.Common.Services.Paths;

public interface IPathCalculator
{
    Task<Queue<IPathStep>> Calculate(IGoal goal);
}
