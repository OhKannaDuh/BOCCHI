using BOCCHI.Buff.Data;
using Ocelot.States.Flow;

namespace BOCCHI.Buff.StateMachine.Handlers;

public class NoCrystalsFoundHandler() : FlowStateHandler<BuffState>(BuffState.NoCrystalsFound)
{
    public override BuffState? Handle()
    {
        return null;
    }
}
