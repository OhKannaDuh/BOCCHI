using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Ocelot.Ipc.RotationSolverReborn;
using Xunit;

namespace BOCCHI.Tests;

public class RotationSolverRebornIpcTests
{
    // Audit: an enum subscriber failure still tries the byte action accepted by other RSR builds.
    [Fact]
    public void EnumFailureRetriesThroughByteSubscriber()
    {
        int enumCalls = 0;
        byte? byteCommand = null;
        ICallGateSubscriber<RSRStateCommandType, object> enumSubscriber = StandIn.For<ICallGateSubscriber<RSRStateCommandType, object>>(s => s
            .Answers("HasAction", true)
            .Answers("InvokeAction", _ => { enumCalls++; throw new InvalidOperationException(); }));
        ICallGateSubscriber<byte, object> byteSubscriber = StandIn.For<ICallGateSubscriber<byte, object>>(s => s
            .Answers("HasAction", true)
            .Answers("InvokeAction", args => { byteCommand = (byte)args[0]!; return null; }));
        int subscribers = 0;
        IDalamudPluginInterface plugin = StandIn.For<IDalamudPluginInterface>(s => s.Answers("GetIpcSubscriber", _ => ++subscribers == 1 ? enumSubscriber : (object)byteSubscriber));
        RotationSolverRebornIpc ipc = new(plugin);
        Assert.True(ipc.IsAvailable);
        RSRStateCommandType command = (RSRStateCommandType)1;
        Assert.True(ipc.ChangeOperatingMode(command));
        Assert.Equal(1, enumCalls);
        Assert.Equal((byte)command, byteCommand);
    }
}
