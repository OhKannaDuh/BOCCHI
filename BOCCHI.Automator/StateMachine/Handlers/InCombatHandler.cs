using BOCCHI.Automator.Data;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Fate;
using Ocelot.Services.UI;
using Ocelot.States.Score;

namespace BOCCHI.Automator.StateMachine.Handlers;

public class InCombatHandler(IObjectTable objects, ICondition conditions, IUIService ui) : ScoreStateHandler<AutomatorState, StatePriority>(AutomatorState.InCombat)
{
    public override StatePriority GetScore()
    {
        if (objects.LocalPlayer == null)
        {
            return StatePriority.Never;
        }

        // Fate check
        unsafe
        {
            var fateManager = FateManager.Instance();
            if (fateManager != null)
            {
                if (fateManager->CurrentFate != null)
                {
                    return StatePriority.Never;
                }
            }
        }

        // Critical Encounter Check

        // Somehow a Mob Farm Check

        return conditions[ConditionFlag.InCombat] ? StatePriority.Always :  StatePriority.Never;
    }

    public override void Handle()
    {
    }

    public override void Render()
    {
        base.Render();

        foreach (var condition in conditions.AsReadOnlySet())
        {
            if (conditions[condition])
            {
                ui.Text(condition);
            }
        }
    }
}
