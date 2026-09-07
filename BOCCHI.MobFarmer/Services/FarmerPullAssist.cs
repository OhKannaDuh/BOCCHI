using BOCCHI.Common.Config;
using BOCCHI.Common.Extensions;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using ECommons.Throttlers;
using FFXIVClientStructs.FFXIV.Client.Game;
using Ocelot.Services.PlayerState;
using Action = Ocelot.Actions.Action;

namespace BOCCHI.MobFarmer.Services;

/// <summary>Tank ranged / Provoke / gap closer during Gathering. No-op on non-tanks.</summary>
public sealed class FarmerPullAssist(
    MobFarmerConfig config,
    IPlayer player,
    ITargetManager targets,
    IObjectTable objects)
{
    public const float PullRange = 20f;

    private const ulong InvalidTargetId = 0xE0000000;

    // Action IDs from Wrath Combo (RoleActions / job helpers).
    private static readonly Action Provoke = new(ActionType.Action, 7533);

    private static readonly Action ShieldLob = new(ActionType.Action, 24);

    private static readonly Action Tomahawk = new(ActionType.Action, 46);

    private static readonly Action Unmend = new(ActionType.Action, 3624);

    private static readonly Action LightningShot = new(ActionType.Action, 16143);

    private static readonly Action Intervene = new(ActionType.Action, 16461);

    private static readonly Action Onslaught = new(ActionType.Action, 7386);

    private static readonly Action Shadowstride = new(ActionType.Action, 36926);

    private static readonly Action Trajectory = new(ActionType.Action, 36934);

    public bool TryPull(IBattleNpc current)
    {
        if (!player.IsTank())
        {
            return false;
        }

        // Already on us — do not re-cast Provoke / gap / ranged every PullRange tick.
        if (current.IsTargetingPlayer(objects.LocalPlayer))
        {
            return false;
        }

        if (!EzThrottler.Throttle("MobFarmer::Pull", 400))
        {
            return false;
        }

        ulong targetId = current.GameObjectId;
        if (targetId == 0 || targetId == InvalidTargetId)
        {
            return false;
        }

        if (config.ShouldHandleTargeting)
        {
            targets.Target = current;
        }

        // One skill per tick, aimed at this enemy rather than a flickered hard target.
        if (config.UseRangedPull && TryRanged() is { } ranged && ranged.CanCast())
        {
            return ranged.Cast(targetId);
        }

        if (config.UseProvoke && Provoke.CanCast())
        {
            return Provoke.Cast(targetId);
        }

        if (config.UseGapCloser && TryGapCloser() is { } gap && gap.CanCast())
        {
            return gap.Cast(targetId);
        }

        return false;
    }

    private Action? TryRanged()
    {
        uint? jobId = player.GetClassJob()?.RowId;
        return jobId switch
        {
            19 or 1 => ShieldLob,
            21 or 3 => Tomahawk,
            32 => Unmend,
            37 => LightningShot,
            _ => null,
        };
    }

    private Action? TryGapCloser()
    {
        uint? jobId = player.GetClassJob()?.RowId;
        return jobId switch
        {
            19 => Intervene,
            21 => Onslaught,
            32 => Shadowstride,
            37 => Trajectory,
            _ => null,
        };
    }
}
