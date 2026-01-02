using BOCCHI.Automator.Data;
using BOCCHI.Automator.Data.StateMemory;
using BOCCHI.Automator.Services;
using BOCCHI.Common.Data.SupportJobs;
using BOCCHI.Common.Data.Zones;
using BOCCHI.Common.Services;
using Dalamud.Plugin.Services;
using ECommons.Throttlers;
using FFXIVClientStructs.FFXIV.Client.Game.InstanceContent;
using Ocelot.Extensions;
using Ocelot.Pathfinding.Extensions;
using Ocelot.Services.Logger;
using Ocelot.Services.Pathfinding;
using Ocelot.Services.UI;
using Ocelot.States.Score;

namespace BOCCHI.Automator.StateMachine.Handlers;

public class ReturningToJobHandler(IAutomatorMemory memory, ISupportJobFactory jobs, ISupportJobChanger changer) : ScoreStateHandler<AutomatorState, StatePriority>(AutomatorState.ReturningToJob)
{
    public override StatePriority GetScore()
    {
        return memory.TryRemember<SupportJobMemory>(out var _) ? StatePriority.AboveNormal : StatePriority.Never;
    }

    public override void Handle()
    {
        if (!EzThrottler.Throttle("ReturningToJobHandler::Gate"))
        {
            return;
        }

        if (!memory.TryRemember<SupportJobMemory>(out var job))
        {
            return;
        }

        if (jobs.TryGetCurrent(out var current) && current.Id == job.Job)
        {
            memory.Forget<SupportJobMemory>();
            return;
        }

        changer.Change(job.Job);
    }
}
