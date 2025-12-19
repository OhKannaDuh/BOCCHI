using System.Linq;
using Dalamud.Game.ClientState.Statuses;

namespace BOCCHI.Data;

public enum PlayerStatus : uint
{
    // Generic
    HoofingIt = 1778,

    // Zone Specific
    DutiesAsAssigned = 4228,
    EnduringFortitude = 4233,
    Fleetfooted = 4239,
    RomeosBallad = 4244,
    BattleBell = 4251,
    ResurrectionRestricted = 4262,
    ResurrectionDenied = 4263,
    PhantomFreelancer = 4242,
    PhantomKnight = 4358,
    PhantomBerserker = 4359,
    PhantomMonk = 4360,
    PhantomRanger = 4361,
    PhantomSamurai = 4362,
    PhantomBard = 4363,
    PhantomGeomancer = 4364,
    PhantomTimeMage = 4365,
    PhantomCannoneer = 4366,
    PhantomChemist = 4367,
    PhantomOracle = 4368,
    PhantomThief = 4369,
    // 7.4 additions
    QuickerStep = 4799, // Dancer buff
    PhantomMysticKnight = 4803,
    PhantomGladiator = 4804,
    PhantomDancer = 4805,
    
}

public static class StatusListExtensions
{
    public static bool Has(this StatusList current, PlayerStatus status)
    {
        return current.HasAny(status);
    }

    public static bool HasAny(this StatusList current, params PlayerStatus[] statuses)
    {
        foreach (var status in statuses)
        {
            if (current.Any(s => s.StatusId == (uint)status))
            {
                return true;
            }
        }

        return false;
    }

    public static IStatus? Get(this StatusList current, PlayerStatus status)
    {
        return current.FirstOrDefault(s => s.StatusId == (uint)status);
    }
}
