using BOCCHI.Buff.Data;

namespace BOCCHI.Buff.Services;

public interface IBuffProvider
{
    IEnumerable<BuffData> GetBuffs();

    BuffData GetBuffForState(BuffState state);

    bool ShouldRefreshAny();
}
