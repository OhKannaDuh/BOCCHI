using BOCCHI.Automator.Data;
using BOCCHI.Automator.Data.Goals;
using BOCCHI.Common.Data.Goals;

namespace BOCCHI.Automator.Services.Goals;

public interface IGoalValidator
{
    bool Validate(IGoal goal);
}
