namespace BOCCHI.Common.Data.Goals;

public interface IGoal
{
     GoalType GoalType { get; }

     string Describe();
}
