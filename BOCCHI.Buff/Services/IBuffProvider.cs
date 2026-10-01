using BOCCHI.Buff.Data;
using Dalamud.Game.ClientState.Objects.SubKinds;

namespace BOCCHI.Buff.Services;

public interface IBuffProvider
{
    IEnumerable<BuffData> GetBuffs();

    BuffData GetBuffForState(BuffState state);

    bool ShouldRefreshAny();

    bool CanUseInquiringMind();

    IEnumerable<BuffData> GetInquiringMindTargetsNeedingRefresh(IPlayerCharacter player, uint maxFreshMinutes);

    bool NeedsInquiringMind(IPlayerCharacter player, uint maxFreshMinutes);

    bool AreInquiringMindTargetsFresh(IPlayerCharacter player);
}
