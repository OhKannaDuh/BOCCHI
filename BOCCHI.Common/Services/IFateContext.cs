using BOCCHI.Common.Data.Fates;
using Dalamud.Game.ClientState.Objects.Types;

namespace BOCCHI.Common.Services;

public interface IFateContext
{
    bool IsInFate();

    FateId? GetFateId();

    IEnumerable<IBattleNpc> GetFateTargets();
}
