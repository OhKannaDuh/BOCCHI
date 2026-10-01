using BOCCHI.Common;
using BOCCHI.Common.Config;
using BOCCHI.Common.Data.StateMemory;
using BOCCHI.Common.Data.SupportJobs;
using BOCCHI.Common.Services;
using Dalamud.Plugin.Services;
using Ocelot.Ipc.RotationSolverReborn;
using Ocelot.Rotation.Services;
using Ocelot.Services.Logger;
using Ocelot.Services.PlayerState;
using Ocelot.Services.PluginStatus;

namespace BOCCHI.Automator.Services;

public class AutoRotationController(
    ICombatRotationSession session,
    AutomatorConfig config,
    UIConfig uiConfig,
    IPlayer player,
    IChatGui chat,
    ICriticalEncounterContext criticalEncounters,
    IFateContext fates,
    IPluginStatus pluginStatus,
    IRotationSolverRebornIpc rsr,
    ISupportJobFactory supportJobs,
    IAutomatorMemory memory,
    ILogger<AutoRotationController> logger
)
{
    private CombatActivity? lastEnabledActivity;

    private string? lastSyncSkipReason;
    private bool CombatSuppressedByActivity =>
        memory.TryRemember<PotChestFarmMemory>(out PotChestFarmMemory _)
        || memory.TryRemember<PendingPotChestFarmMemory>(out PendingPotChestFarmMemory _);

    public void PrepareForIllegalMode()
    {
        session.OverwriteBossModPresets = config.UpdateBossModPresetsAutomatically;
        session.MovementSettings = BossModMovement.From(config, player.IsMelee(), player.GetClassJob()?.RowId);
        if (!config.CombatAutorotation.UsesCombatAutomation()
            || !CombatAutorotationSetup.ValidatePlugins(
                config.CombatAutorotation,
                pluginStatus,
                rsr,
                message => CombatAutorotationSetup.PrintError(chat, uiConfig, message)))
        {
            return;
        }

        session.Prepare(CombatAutorotationSetup.ToRecipe(config));
        session.Tick(CurrentPhantomJobId());
        SyncActivityCombat();
    }

    public void TeardownForIllegalMode() => session.Teardown();

    public void OnRevived()
    {
        session.ClearJobAppliedCache();
        lastEnabledActivity = null;
    }

    public void EnableForFate() => EnableActivity(CombatActivity.Fate);

    public void EnableForCriticalEncounter() => EnableActivity(CombatActivity.CriticalEncounter);

    public void EnableForSelfDefence() => EnableActivity(CombatActivity.Fate);

    public void DisableAi()
    {
        if (!CombatSuppressedByActivity
            && memory.TryRemember<SuspendTravelForActivityMemory>(out SuspendTravelForActivityMemory _)
            && (criticalEncounters.IsInCriticalEncounter()
                || fates.IsInFate()
                || memory.TryRemember<CommittedFateMemory>(out CommittedFateMemory _)
                || memory.TryRemember<CommittedCriticalEncounterMemory>(out CommittedCriticalEncounterMemory _)))
        {
            if (lastSyncSkipReason != "keep-in-activity")
            {
                lastSyncSkipReason = "keep-in-activity";
                logger.Debug("Combat AI Disable skipped — still In FATE/CE with travel suspended");
            }

            return;
        }

        if (lastEnabledActivity is not null)
        {
            logger.Debug("Combat AI Disable (was {Activity})", lastEnabledActivity);
            lastEnabledActivity = null;
        }

        lastSyncSkipReason = null;
        session.Disable();
    }

    public void Tick()
    {
        session.OverwriteBossModPresets = config.UpdateBossModPresetsAutomatically;
        session.MovementSettings = BossModMovement.From(config, player.IsMelee(), player.GetClassJob()?.RowId);
        SyncActivityCombat();
        session.Tick(CurrentPhantomJobId());
    }

    private void EnableActivity(CombatActivity activity)
    {
        // Re-issuing Enable every tick made RSR flip Targeting Henched ↔ Off in FATEs (Lumi).
        if (lastEnabledActivity == activity)
        {
            return;
        }

        logger.Debug(
            "Combat AI Enable activity={Activity} recipe={Recipe}",
            activity,
            config.CombatAutorotation);
        lastEnabledActivity = activity;
        lastSyncSkipReason = null;
        session.Enable(activity);
    }

    private void SyncActivityCombat()
    {
        if (CombatSuppressedByActivity)
        {
            LogSyncSkip("pot-farm");
            return;
        }

        if (memory.TryRemember<GoalPathStepMemory>(out GoalPathStepMemory _)
            || memory.TryRemember<WaitingForCriticalEncounterMemory>(out WaitingForCriticalEncounterMemory _)
            || memory.TryRemember<WaitingForPotFateMemory>(out WaitingForPotFateMemory _))
        {
            LogSyncSkip("path-or-wait");
            return;
        }

        if (!memory.TryRemember<SuspendTravelForActivityMemory>(out SuspendTravelForActivityMemory _))
        {
            LogSyncSkip("no-suspend-travel");
            return;
        }

        if (memory.TryRemember<CommittedCriticalEncounterMemory>(out CommittedCriticalEncounterMemory _)
            || criticalEncounters.IsInCriticalEncounter())
        {
            EnableActivity(CombatActivity.CriticalEncounter);
            return;
        }

        if (memory.TryRemember<CommittedFateMemory>(out CommittedFateMemory _)
            || fates.IsInFate())
        {
            EnableActivity(CombatActivity.Fate);
        }
    }

    private void LogSyncSkip(string reason)
    {
        if (lastSyncSkipReason == reason)
        {
            return;
        }

        lastSyncSkipReason = reason;
        logger.Debug("Combat AI Sync skipped: {Reason}", reason);
    }

    private uint? CurrentPhantomJobId() =>
        supportJobs.TryGetCurrent(out SupportJob current) ? current.Id.RowId() : null;
}
