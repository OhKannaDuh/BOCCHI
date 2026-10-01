using BOCCHI.Common.Config;
using BOCCHI.Common.Data.Zones;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using ECommons.Throttlers;
using Ocelot.Actions;
using Ocelot.Extensions;
using Ocelot.Ipc.VNavmesh;
using Ocelot.Lifecycle;
using Ocelot.Services.Logger;
using Ocelot.Services.PlayerState;
using System.Numerics;
using ECommonsPlayer = ECommons.GameHelpers.Player;

namespace BOCCHI.Common.Services;

public sealed class StuckJumpAssist(
    IVNavmeshIpc vnav,
    IPlayer player,
    ICondition conditions,
    IZoneProvider zones,
    MovementConfig config,
    ILogger<StuckJumpAssist> logger
) : IOnUpdate
{
    private const int RetryThrottleMs = 2000;

    private const float ProgressThreshold = 1f;

    private const int MaxJumpsAtSnag = 5;

    private static readonly TimeSpan GiveUpCooldown = TimeSpan.FromSeconds(30);

    private const float GiveUpRadius = 5f;

    private Vector3 lastPosition;

    private DateTime movedAtUtc = DateTime.MinValue;

    private Vector3 snagAnchor;

    private int jumpsAtSnag;

    private Vector3 giveUpNear;

    private DateTime giveUpUntilUtc = DateTime.MinValue;

    public UpdateLimit UpdateLimit =>
        new()
        {
            Mode = UpdateLimitMode.Milliseconds,
            Limit = 250,
        };

    public void Update()
    {
        if (!config.ShouldJumpWhenStuck || !zones.GetZone().IsOccultCrescentZone() || !vnav.IsRunning())
        {
            movedAtUtc = DateTime.MinValue;
            jumpsAtSnag = 0;
            return;
        }

        if (conditions[ConditionFlag.Casting]
            || conditions[ConditionFlag.Casting87]
            || conditions[ConditionFlag.BetweenAreas]
            || conditions[ConditionFlag.Occupied]
            || conditions[ConditionFlag.OccupiedInQuestEvent]
            || conditions[ConditionFlag.Unconscious]
            || ECommonsPlayer.IsJumping)
        {
            movedAtUtc = DateTime.MinValue;
            return;
        }

        Vector3 position = player.Position;

        if (DateTime.UtcNow < giveUpUntilUtc && position.Distance2D(giveUpNear) <= GiveUpRadius)
        {
            return;
        }

        if (movedAtUtc == DateTime.MinValue || position.Distance2D(lastPosition) > ProgressThreshold)
        {
            lastPosition = position;
            movedAtUtc = DateTime.UtcNow;
            jumpsAtSnag = 0;
            return;
        }

        if (DateTime.UtcNow - movedAtUtc < TimeSpan.FromSeconds(config.JumpWhenStuckSeconds)
            || !EzThrottler.Throttle("StuckJumpAssist::Jump", RetryThrottleMs))
        {
            return;
        }

        if (jumpsAtSnag == 0 || position.Distance2D(snagAnchor) > ProgressThreshold)
        {
            snagAnchor = position;
            jumpsAtSnag = 0;
        }

        jumpsAtSnag++;
        if (jumpsAtSnag > MaxJumpsAtSnag)
        {
            logger.Debug(
                "Still stuck at {Pos:F0} after {Count} jumps — stopping pathfind",
                position,
                MaxJumpsAtSnag);
            vnav.Stop();
            giveUpNear = position;
            giveUpUntilUtc = DateTime.UtcNow + GiveUpCooldown;
            jumpsAtSnag = 0;
            movedAtUtc = DateTime.MinValue;
            return;
        }

        logger.Debug(
            "Stuck at {Pos:F0} with vnav still running — jumping to break free ({Attempt}/{Max})",
            position,
            jumpsAtSnag,
            MaxJumpsAtSnag);
        Actions.Jump.Cast();

        movedAtUtc = DateTime.UtcNow;
    }
}
