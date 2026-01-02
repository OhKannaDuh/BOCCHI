using BOCCHI.Automator.Data;
using BOCCHI.Common.Data.Zones;
using Dalamud.Plugin.Services;
using Ocelot.Extensions;
using Ocelot.Pathfinding.Extensions;
using Ocelot.Services.Logger;
using Ocelot.Services.Pathfinding;
using Ocelot.Services.UI;
using Ocelot.States.Score;

namespace BOCCHI.Automator.StateMachine.Handlers;

public class EntryHandler() : ScoreStateHandler<AutomatorState, StatePriority>(AutomatorState.Entry)
{
    public override StatePriority GetScore()
    {
        return StatePriority.Never;
    }

    public override void Handle()
    {
    }
}
