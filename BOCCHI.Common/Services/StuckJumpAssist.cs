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

/// <summary>
///     Jump when vnav reports moving but the character is stuck on mesh it thinks is walkable (#185).
/// </summary>
public sealed class StuckJumpAssist(
    IVNavmeshIpc vnav,
    IPlayer player,
    ICondition conditions,
    IZoneProvider zones,
    MovementConfig config,
    ILogger<StuckJumpAssist> logger
) : IOnUpdate
{
    /// <summary>Leave time to land and for vnav to make real progress before hopping again.</summary>
    private const int RetryThrottleMs = 2000;

    /// <summary>Horizontal movement that counts as progress. Generous — a snag moves you nowhere.</summary>
    private const float ProgressThreshold = 1f;

    /// <summary>Jumps at one snag before stopping pathfind so callers can repath or skip.</summary>
    private const int MaxJumpsAtSnag = 5;

    /// <summary>How long to refuse more hops near a give-up spot (avoids jump→repath→jump loops).</summary>
    private static readonly TimeSpan GiveUpCooldown = TimeSpan.FromSeconds(30);

    /// <summary>Still “the same snag” if we have not walked this far from the give-up point.</summary>
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

        // Casting is the one thing a jump would actively break, and the rest mean the character is
        // not under our control anyway — standing still then is expected, not stuck.
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

        // 2D: falling is vertical-only and cannot be helped by jumping, so it must not read as
        // progress — and a snag against geometry stops horizontal movement specifically.
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

        // Give the hop a chance to land before judging progress again.
        movedAtUtc = DateTime.UtcNow;
    }
}
