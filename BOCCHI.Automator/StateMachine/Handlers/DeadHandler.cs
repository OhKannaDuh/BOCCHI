using BOCCHI.Automator.Data;
using Dalamud.Game.ClientState.Conditions;
using Ocelot.Services.PlayerState;
using Ocelot.States.Score;

namespace BOCCHI.Automator.StateMachine.Handlers;

public class DeadHandler(IPlayer player) : ScoreStateHandler<AutomatorState, StatePriority>(AutomatorState.Dead)
{
    public override StatePriority GetScore()
    {
        return player.Conditions[ConditionFlag.Unconscious] ? StatePriority.Always : StatePriority.Never;
    }

    public override void Handle()
    {
    }
}
