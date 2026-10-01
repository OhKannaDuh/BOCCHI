using System.Numerics;
using Dalamud.Game.Chat;
using Dalamud.Plugin.Services;
using Ocelot.Lifecycle;
using Ocelot.Services.PlayerState;

namespace BOCCHI.Automator.Services.PotTreasure;

public sealed class PotTreasureHintTracker(IChatGui chat, IPlayer player) : IOnStart, IOnStop
{
    private readonly object gate = new();

    private bool armed;

    private int revision;

    private PotTreasureHintEvent? lastEvent;

    public int Revision
    {
        get
        {
            lock (gate)
            {
                return revision;
            }
        }
    }

    public void OnStart() => chat.LogMessage += OnLogMessage;

    public void OnStop()
    {
        chat.LogMessage -= OnLogMessage;
        Disarm();
    }

    public void Arm()
    {
        lock (gate)
        {
            armed = true;
            revision = 0;
            lastEvent = null;
        }
    }

    public void Disarm()
    {
        lock (gate)
        {
            armed = false;
            lastEvent = null;
        }
    }

    public bool TryGetEventSince(int baselineRevision, out PotTreasureHintEvent hintEvent)
    {
        lock (gate)
        {
            if (lastEvent is { } e && e.Revision > baselineRevision)
            {
                hintEvent = e;
                return true;
            }
        }

        hintEvent = null!;
        return false;
    }

    private void OnLogMessage(ILogMessage message)
    {
        if (!TryClassify(message, player.Position, out PotTreasureHintEvent parsed))
        {
            return;
        }

        lock (gate)
        {
            if (!armed)
            {
                return;
            }

            revision++;
            parsed.Revision = revision;
            lastEvent = parsed;
        }
    }

    private static bool TryClassify(ILogMessage message, Vector3 playerPosition, out PotTreasureHintEvent parsed)
    {
        parsed = null!;
        uint id = message.LogMessageId;

        if (id == PotTreasureIds.CofferRevealLogMessageId)
        {
            parsed = new PotTreasureHintEvent
            {
                Kind = PotTreasureHintKind.CofferReveal,
                LogMessageId = id,
            };
            return true;
        }

        if (id == PotTreasureIds.ElixirPromptLogMessageId)
        {
            parsed = new PotTreasureHintEvent
            {
                Kind = PotTreasureHintKind.ElixirPrompt,
                LogMessageId = id,
            };
            return true;
        }

        if (id == PotTreasureIds.BonusOfferLogMessageId)
        {
            parsed = new PotTreasureHintEvent
            {
                Kind = PotTreasureHintKind.BonusOffer,
                LogMessageId = id,
            };
            return true;
        }

        PotTreasureDistanceBucket distance = PotTreasureIds.MapDistance(id);
        if (distance == PotTreasureDistanceBucket.Unknown)
        {
            return false;
        }

        if (!message.TryGetIntParameter(0, out int directionValue))
        {
            return false;
        }

        PotTreasureDirection direction = PotTreasureIds.MapDirection(directionValue);
        if (direction == PotTreasureDirection.Unknown)
        {
            return false;
        }

        parsed = new PotTreasureHintEvent
        {
            Kind = PotTreasureHintKind.Hint,
            LogMessageId = id,
            Direction = direction,
            Distance = distance,
            Origin = playerPosition,
        };
        return true;
    }
}
