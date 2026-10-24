using BOCCHI.MobFarmer.Data;
using Ocelot.Lifecycle;
using System.Numerics;

namespace BOCCHI.MobFarmer.Data
{
    public enum FarmerPhase
    {
        Waiting,
        Buffing,
        Gathering,
        Stacking,
        Fighting
    }

    public enum FarmerYieldReason
    {
        None = 0,
        Pots,
        TreasureSight,
        TreasureHunt,
        CrystalBuffs,
        Shopping,
    }
}

namespace BOCCHI.MobFarmer.Services
{
    public interface IMobFarmer : IOnUpdate
    {
        bool Running { get; }

        bool Suspended { get; }

        FarmerYieldReason YieldReason { get; }

        Vector3 StartingPoint { get; }

        Vector3? StackPoint { get; }

        string? CurrentSpotName { get; }

        int EffectiveMinimumMobsToStartFight { get; }

        bool NeedsApproachSpot { get; }

        void MarkArrivedAtSpot();

        FarmerPhase Phase { get; }

        bool CanAcceptYield { get; }

        void RequestAiPull();

        void Toggle();

        void SetSuspended(bool suspended, FarmerYieldReason reason = FarmerYieldReason.None);

        void Render();
    }
}
