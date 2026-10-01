using BOCCHI.Common.Data.OccultCrescent;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using Ocelot.Extensions;

namespace BOCCHI.Automator.Services;

internal static class RaiseableCorpses
{
    public const float CastRangeYalms = 28f;

    public const float SearchRangeYalms = CastRangeYalms + 15f;

    public static bool Any(IObjectTable objects) => FindNearest(objects) != null;

    public static IPlayerCharacter? FindNearest(IObjectTable objects)
    {
        if (objects.LocalPlayer is not { } self)
        {
            return null;
        }

        IPlayerCharacter? best = null;
        float bestDist = float.MaxValue;

        foreach (IGameObject obj in objects)
        {
            if (obj is not IPlayerCharacter player
                || player.EntityId == self.EntityId
                || !player.IsDead
                || !player.IsTargetable
                || player.StatusList.Has(PlayerStatuses.Raise))
            {
                continue;
            }

            float dist = self.Position.Distance2D(player.Position);
            if (dist > SearchRangeYalms || dist >= bestDist)
            {
                continue;
            }

            best = player;
            bestDist = dist;
        }

        return best;
    }
}
