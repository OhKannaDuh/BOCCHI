using BOCCHI.Common.Config;
using BOCCHI.Common.Data.SupportJobs;
using Ocelot.Actions;
using Action = Ocelot.Actions.Action;

namespace BOCCHI.Buff.Data;

public readonly struct BuffData
{
    public uint StatusId { get; init; }

    public SupportJobId SupportJobId { get; init; }

    public uint RequiredLevel { get; init; }

    public Func<BuffConfig, bool> ShouldApply { get; init; }

    public BuffState State { get; init; }

    public Action Action { get; init; }

    public readonly static BuffData RomeosBallad = new()
    {
        StatusId = 4244,
        RequiredLevel = 2,
        SupportJobId = SupportJobId.PhantomBard,
        ShouldApply = config => config.ShouldApplyRomeosBallad(),
        State = BuffState.ApplyingRomeosBallad,
        Action = Actions.PhantomActionII, // Romeo's Ballad
    };

    public readonly static BuffData Fleetfooted = new()
    {
        StatusId = 4239,
        RequiredLevel = 3,
        SupportJobId = SupportJobId.PhantomMonk,
        ShouldApply = config => config.ShouldApplyFleetfooted(),
        State = BuffState.ApplyingFleetfooted,
        Action = Actions.PhantomActionIII, // Counterstance
    };

    public readonly static BuffData EnduringFortitude = new()
    {
        StatusId = 4233,
        RequiredLevel = 2,
        SupportJobId = SupportJobId.PhantomKnight,
        ShouldApply = config => config.ShouldApplyEnduringFortitude(),
        State = BuffState.ApplyingEnduringFortitude,
        Action = Actions.PhantomActionII, // Pray
    };

    public readonly static BuffData QuickerStep = new()
    {
        StatusId = 4799,
        RequiredLevel = 2,
        SupportJobId = SupportJobId.PhantomDancer,
        ShouldApply = config => config.ShouldApplyQuickerStep(),
        State = BuffState.ApplyingQuickerStep,
        Action = Actions.PhantomActionII, // Quickstep
    };

    public readonly static IEnumerable<BuffData> All =
    [
        RomeosBallad,
        Fleetfooted,
        EnduringFortitude,
        QuickerStep
    ];
}
