using BOCCHI.Common.Data.CriticalEncounters;
using BOCCHI.Common.Data.Fates;

namespace BOCCHI.Common.Data.Goals;

public abstract record GoalType;
public sealed record FateGoal(FateId id) : GoalType;
public sealed record CriticalEncounterGoal(CriticalEncounterId id) : GoalType;


