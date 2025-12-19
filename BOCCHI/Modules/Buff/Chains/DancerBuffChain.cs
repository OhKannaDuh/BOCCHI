using BOCCHI.ActionHelpers;
using BOCCHI.Data;

namespace BOCCHI.Modules.Buff.Chains;

public class DancerBuffChain(BuffModule module) : BuffChain(Job.Dancer, PlayerStatus.QuickerStep, Actions.Dancer.Quickstep)
{
    protected override bool ShouldRun()
    {
        return module.Config.ApplyQuickerStep;
    }
}
