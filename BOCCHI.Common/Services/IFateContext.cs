using BOCCHI.Common.Data.Fates;
using Dalamud.Game.ClientState.Objects.Types;

namespace BOCCHI.Common.Services;

public interface IFateContext
{
    bool IsInFate();

    FateId? GetFateId();

    bool IsInCombatWith(FateId id);

    IEnumerable<IBattleNpc> GetTargets();

    IEnumerable<IBattleNpc> GetTargetsFor(FateId id);
}
