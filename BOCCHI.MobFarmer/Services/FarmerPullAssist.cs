using BOCCHI.Common.Config;
using BOCCHI.Common.Extensions;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using ECommons.Throttlers;
using FFXIVClientStructs.FFXIV.Client.Game;
using Ocelot.Services.PlayerState;
using Action = Ocelot.Actions.Action;

namespace BOCCHI.MobFarmer.Services;

public sealed class FarmerPullAssist(
    MobFarmerConfig config,
    IPlayer player,
    ITargetManager targets,
    IObjectTable objects)
{
    public const float PullRange = 20f;

    private const ulong InvalidTargetId = 0xE0000000;

    private static readonly Action Provoke = new(ActionType.Action, 7533);

    private static readonly Action ShieldLob = new(ActionType.Action, 24);

    private static readonly Action Tomahawk = new(ActionType.Action, 46);

    private static readonly Action Unmend = new(ActionType.Action, 3624);

    private static readonly Action LightningShot = new(ActionType.Action, 16143);

    private static readonly Action Intervene = new(ActionType.Action, 16461);

    private static readonly Action Onslaught = new(ActionType.Action, 7386);

    private static readonly Action Shadowstride = new(ActionType.Action, 36926);

    private static readonly Action Trajectory = new(ActionType.Action, 36934);

    // A pull lands a moment after the cast; trying again meanwhile spent Tomahawk, Provoke and the gap closer on one mob.
    private static readonly TimeSpan PullSettle = TimeSpan.FromSeconds(2.5);

    private ulong lastPulledId;

    private DateTime lastPullUtc = DateTime.MinValue;

    public bool TryPull(IBattleNpc current)
    {
        if (!player.IsTank())
        {
            return false;
        }

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

        if (targetId == lastPulledId && DateTime.UtcNow - lastPullUtc < PullSettle)
        {
            return false;
        }

        if (config.ShouldHandleTargeting)
        {
            targets.Target = current;
        }

        Action? tool = config.UseRangedPull && TryRanged() is { } ranged && ranged.CanCast() ? ranged
            : config.UseProvoke && Provoke.CanCast() ? Provoke
            : config.UseGapCloser && TryGapCloser() is { } gap && gap.CanCast() ? gap
            : null;
        if (tool == null)
        {
            return false;
        }

        // UseAction can report false for an action it queued, so the wait starts on the attempt.
        lastPulledId = targetId;
        lastPullUtc = DateTime.UtcNow;
        return tool.Cast(targetId);
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
