using BOCCHI.Common.Data.Fates;

namespace BOCCHI.Common.Services;

public interface IFateRepository
{
    event Action<Fate> FateAdded;

    event Action<FateId> FateRemoved;

    IReadOnlyList<Fate> Snapshot();

    bool HasFate(FateId id);
}
