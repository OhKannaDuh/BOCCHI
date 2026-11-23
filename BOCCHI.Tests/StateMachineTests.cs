using Ocelot.Services.Translation;
using Ocelot.Services.UI;
using Ocelot.States.Score;
using Xunit;

namespace BOCCHI.Tests;

public class StateMachineTests
{
    public enum TestState
    {
        A,
        B,
        C,
    }

    // Audit 37: one handler throwing in GetScore stopped every state change, even to Dead.
    [Fact]
    public void AThrowingStateDoesNotBlockTheOthers()
    {
        ScoreStateMachine<TestState, int> machine = Machine(
            new Fixed(TestState.A, () => 1),
            new Fixed(TestState.B, () => 5),
            new Fixed(TestState.C, () => throw new InvalidOperationException("broken")));

        machine.Update();

        Assert.Equal(TestState.B, machine.State);
    }

    // Audit 38: on a tie the first registered handler won, so the current state was left for nothing.
    [Fact]
    public void OnATieTheCurrentStateStays()
    {
        int bScore = 5;
        ScoreStateMachine<TestState, int> machine = Machine(
            new Fixed(TestState.A, () => 5),
            new Fixed(TestState.B, () => bScore));

        bScore = 9;
        machine.Update();
        Assert.Equal(TestState.B, machine.State);

        bScore = 5;
        machine.Update();
        Assert.Equal(TestState.B, machine.State);
    }

    private static ScoreStateMachine<TestState, int> Machine(params Fixed[] handlers) =>
        new(
            TestState.A,
            StandIn.For<ITranslator<ScoreStateMachine<TestState, int>>>(),
            StandIn.For<IUIService>(),
            handlers,
            new QuietLog<ScoreStateMachine<TestState, int>>());

    private sealed class Fixed(TestState state, Func<int> score) : ScoreStateHandler<TestState, int>(state)
    {
        public override int GetScore() => score();

        public override void Handle()
        {
        }
    }
}
