using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Statuses;
using Ocelot.Extensions;

namespace BOCCHI.Common.Extensions;

public static class BattleNpcExtensions
{
    public static bool HasTarget(this IGameObject obj) => obj.TargetObject != null;

    public static bool IsTargetingPlayer(this IGameObject obj, IGameObject? player) =>
        player != null && obj.TargetObject?.Address == player.Address;
}

public static class CharacterStatusExtensions
{
    public static uint GetRemainingMinutes(this IPlayerCharacter player, uint statusId)
    {
        if (!player.StatusList.TryGet(statusId, out IStatus status))
        {
            return 0;
        }

        return (uint)TimeSpan.FromSeconds(status.RemainingTime).TotalMinutes;
    }
}
