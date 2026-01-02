using BOCCHI.Common.Services.Paths;

namespace BOCCHI.Common.Data.Paths;

public interface IPathStep
{
    PathStepType PathStepData { get;  }

    PathStepKind Kind { get; init; }

    string Describe();

    bool TryExecute(IPathStepExecutor executor);
}
