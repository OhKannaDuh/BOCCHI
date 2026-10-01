using Dalamud.Game.ClientState.Objects.Types;

namespace BOCCHI.MobFarmer.Services;

public interface IMobScanner
{
    IReadOnlyList<IBattleNpc> Mobs { get; }

    IReadOnlyList<IBattleNpc> InCombat { get; }

    IReadOnlyList<IBattleNpc> NotInCombat { get; }

    IReadOnlyList<IBattleNpc> Contested { get; }

    void Update();
}
