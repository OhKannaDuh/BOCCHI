using BOCCHI.Automator.Services;
using BOCCHI.Buff.Services;
using BOCCHI.Common.Config;
using BOCCHI.Common.Data.Fates;
using BOCCHI.Common.Data.SupportJobs;
using BOCCHI.Common.Data.Zones;
using BOCCHI.Common.Services;
using BOCCHI.MobFarmer.Data;
using BOCCHI.MobFarmer.Services;
using BOCCHI.Treasure.ChainRecipes;
using BOCCHI.Treasure.Hunt;
using BOCCHI.Treasure.Services;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using Ocelot.Chain;
using Ocelot.Lifecycle;

namespace BOCCHI.Services;

/// <summary>
///     Yields Mob Farmer to pots, Treasure Sight, Treasure Hunt, or knowledge-crystal buffs, then resumes.
/// </summary>
public sealed class MobFarmerYieldService
(
    IMobFarmer farmer,
    IPotsTreasureMode pots,
    ITreasureHunter hunter,
    ITreasureTracker tracker,
    IBuffRunner buffRunner,
    IBuffProvider buffs,
    IPotCycleTracker potCycle,
    IFateRepository fates,
    IZoneProvider zones,
    IChainManager chainManager,
    IChainFactory chains,
    ISupportJobFactory supportJobs,
    ISupportJobChanger supportJobChanger,
    ICondition conditions,
    MobFarmerConfig farmerConfig,
    PotsConfig potsConfig,
    BuffConfig buffConfig,
    TreasureConfig treasureConfig
) : IOnUpdate
{
    public int Order => -10;

    private DateTimeOffset nextHuntAt = DateTimeOffset.MinValue;

    private DateTimeOffset nextSightAt = DateTimeOffset.MinValue;

    private bool startedPots;

    private bool startedHunt;

    private bool startedSight;

    private bool startedBuffs;

    private bool sawRunning;

    private Task<ChainResult>? sightChain;

    /// <summary>Phantom job to restore after Sight when the chain fails before its restore step.</summary>
    private SupportJobId? pendingSightRestoreJob;

    private TimeSpan HuntIntervalMinutes =>
        TimeSpan.FromMinutes(Math.Max(1, farmerConfig.TreasureHuntIntervalMinutes));

    private TimeSpan SightIntervalMinutes =>
        TimeSpan.FromMinutes(Math.Max(1, farmerConfig.TreasureSightIntervalMinutes));

    public void Update()
    {
        if (!farmer.Running)
        {
            sawRunning = false;
            AbortYields();
            return;
        }

        if (!sawRunning)
        {
            sawRunning = true;
            nextHuntAt = DateTimeOffset.UtcNow + HuntIntervalMinutes;
            // Cast Sight as soon as yield is allowed — do not wait a full interval first.
            nextSightAt = DateTimeOffset.MinValue;
        }

        if (farmer.Suspended)
        {
            TickSuspended();
            return;
        }

        // Finish an in-flight Sight chain even if packs are nearby again (otherwise Sight
        // completion never clears and the farmer stays suspended / looks like Sight "never casts").
        if (startedSight && sightChain is { IsCompleted: true })
        {
            FinishSightChain();
        }

        if (!farmer.CanAcceptYield)
        {
            return;
        }

        if (farmerConfig.YieldToPots && NeedsPotWork())
        {
            farmer.SetSuspended(true, FarmerYieldReason.Pots);
            if (pots.StartManagedFromFarmer())
            {
                startedPots = true;
            }
            else
            {
                farmer.SetSuspended(false);
            }

            return;
        }

        if (farmerConfig.YieldToCrystalBuffs && buffConfig.ShouldAutomateBuffs && buffs.ShouldRefreshAny())
        {
            if (!buffRunner.CanStart)
            {
                return;
            }

            farmer.SetSuspended(true, FarmerYieldReason.CrystalBuffs);
            buffRunner.Start();
            startedBuffs = true;
            return;
        }

        if (farmerConfig.YieldToTreasureHunt && HuntIsDue())
        {
            farmer.SetSuspended(true, FarmerYieldReason.TreasureHunt);
            hunter.ManagedByMobFarmer = true;
            hunter.StartManaged();
            startedHunt = hunter.Running;
            if (!startedHunt)
            {
                hunter.ManagedByMobFarmer = false;
                farmer.SetSuspended(false);
                return;
            }

            nextHuntAt = DateTimeOffset.UtcNow + HuntIntervalMinutes;
            return;
        }

        if (farmerConfig.CastTreasureSightAtFarm && TryBeginTreasureSight())
        {
            farmer.SetSuspended(true, FarmerYieldReason.TreasureSight);
            startedSight = true;
        }
    }

    private void TickSuspended()
    {
        switch (farmer.YieldReason)
        {
            case FarmerYieldReason.Pots:
                if (startedPots && !pots.ManagedByMobFarmer)
                {
                    startedPots = false;
                    farmer.SetSuspended(false);
                }

                break;

            case FarmerYieldReason.TreasureSight:
                if (startedSight && sightChain is { IsCompleted: true })
                {
                    FinishSightChain();
                }

                break;

            case FarmerYieldReason.TreasureHunt:
                if (startedHunt && (!hunter.Running || !hunter.ManagedByMobFarmer))
                {
                    startedHunt = false;
                    if (farmer.Suspended)
                    {
                        farmer.SetSuspended(false);
                    }
                }

                break;

            case FarmerYieldReason.CrystalBuffs:
                if (startedBuffs && !buffRunner.IsRunning)
                {
                    startedBuffs = false;
                    farmer.SetSuspended(false);
                }

                break;

            case FarmerYieldReason.Shopping:
                // Shopping may have interrupted a crystal-buff yield — clear the latch so we
                // do not think buffs are still in progress after NotifyShoppingEnded (#203).
                if (startedBuffs)
                {
                    startedBuffs = false;
                    if (buffRunner.IsRunning)
                    {
                        buffRunner.Stop();
                    }
                }

                break;
        }
    }

    private void AbortYields()
    {
        nextHuntAt = DateTimeOffset.MinValue;
        nextSightAt = DateTimeOffset.MinValue;
        if (startedPots)
        {
            startedPots = false;
            pots.StopManagedFromFarmer();
        }

        if (startedSight)
        {
            startedSight = false;
            chainManager.CancelWhere(name => name.StartsWith("MobFarmer::TreasureSight", StringComparison.Ordinal));
            sightChain = null;
            TryRestorePendingSightJob();
            pendingSightRestoreJob = null;
        }

        if (startedHunt)
        {
            startedHunt = false;
            if (hunter.ManagedByMobFarmer && hunter.Running)
            {
                hunter.Toggle();
            }

            hunter.ManagedByMobFarmer = false;
        }

        if (startedBuffs)
        {
            startedBuffs = false;
            if (buffRunner.IsRunning)
            {
                buffRunner.Stop();
            }
        }
    }

    private bool TryBeginTreasureSight()
    {
        if (startedSight || sightChain is { IsCompleted: false })
        {
            return false;
        }

        if (DateTimeOffset.UtcNow < nextSightAt)
        {
            return false;
        }

        if (!SupportJobTreasureSight.CanCast(supportJobs))
        {
            return false;
        }

        // Do not start the chain until dismount + job-swap gates pass — otherwise a step
        // spins for 15s (Dismount / ToFreelancer / RestoreJob) and the farm sits idle.
        if (DismountAssist.TryDismount(conditions)
            || PhantomJobChangeGate.IsBlocked(conditions))
        {
            return false;
        }

        pendingSightRestoreJob = null;
        if (supportJobs.TryGetCurrent(out SupportJob current)
            && current.Id != SupportJobId.PhantomFreelancer)
        {
            pendingSightRestoreJob = current.Id;
        }

        // Do not gate on fill % or an existing Sight reading — this yield is how Mob Farmer
        // refreshes counts (and the first cast of a session). Timed Treasure Hunt still uses
        // TreasureHuntFillGate.
        sightChain = chainManager.Manage(
            chains.Create("MobFarmer::TreasureSight")
                .Then<HuntTreasureSightChain>());
        return true;
    }

    private void FinishSightChain()
    {
        startedSight = false;
        sightChain = null;
        TryRestorePendingSightJob();
        pendingSightRestoreJob = null;
        nextSightAt = DateTimeOffset.UtcNow + SightIntervalMinutes;
        if (farmer.Suspended && farmer.YieldReason == FarmerYieldReason.TreasureSight)
        {
            farmer.SetSuspended(false);
        }
    }

    private void TryRestorePendingSightJob()
    {
        if (pendingSightRestoreJob is not { } id)
        {
            return;
        }

        if (!supportJobs.TryGetCurrent(out SupportJob current)
            || current.Id != SupportJobId.PhantomFreelancer)
        {
            return;
        }

        supportJobChanger.Change(id);
    }

    private bool NeedsPotWork()
    {
        IZone zone = zones.GetZone();
        if (fates.Snapshot().Any(f => zone.IsPotFate(f.Id.Value)))
        {
            return true;
        }

        PotCycleSnapshot cycle = potCycle.Snapshot;
        if (cycle.CurrentActivePotFateId != 0)
        {
            return true;
        }

        if (!cycle.HasPredictedNextPot)
        {
            return false;
        }

        return PotFallbackWindow.ShouldPreposition(
            cycle,
            DateTimeOffset.UtcNow,
            potsConfig.PotSpawnLeadMinutes,
            potFarmingEnabled: true);
    }

    private bool HuntIsDue()
    {
        if (DateTimeOffset.UtcNow < nextHuntAt)
        {
            return false;
        }

        if (!hunter.IsVnavAvailable || !hunter.IsVnavReady)
        {
            return false;
        }

        if (!tracker.CountInitialised)
        {
            return false;
        }

        return TreasureHuntFillGate.MeetsMinimumFill(tracker, treasureConfig);
    }
}
