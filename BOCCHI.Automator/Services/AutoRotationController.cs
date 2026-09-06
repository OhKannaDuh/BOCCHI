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

/// <summary>
///     Illegal Mode adapter: config → <see cref="ICombatRotationSession"/> plus FATE/CE/travel hooks.
/// </summary>
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
    /// <summary>
    ///     Pot chest farming owns the character outright — it walks to reveals and opens them, and
    ///     leftover AutoTarget / AI movement from the pot FATE fights that. The FATE is usually still
    ///     up while farming, so this has to beat the in-activity guards below.
    /// </summary>
    private bool CombatSuppressedByActivity =>
        memory.TryRemember<PotChestFarmMemory>(out PotChestFarmMemory _)
        || memory.TryRemember<PendingPotChestFarmMemory>(out PendingPotChestFarmMemory _);

    public void PrepareForIllegalMode()
    {
        session.OverwriteBossModPresets = config.UpdateBossModPresetsAutomatically;
        session.MovementSettings = BossModMovement.From(config, player.IsMelee(), player.GetClassJob()?.RowId);
        if (!config.CombatAutorotation.UsesCombatAutomation() || !ValidatePluginsForConfig())
        {
            return;
        }

        session.Prepare(ToRecipe(config.CombatAutorotation));
        session.Tick(CurrentPhantomJobId());
        SyncActivityCombat();
    }

    public void TeardownForIllegalMode() => session.Teardown();

    /// <summary>
    ///     After raise: drop job apply latches so the next In CE / Sync Enable re-issues RSR Henched
    ///     (RSR can ignore Henched while unconscious while we still cached success).
    /// </summary>
    public void OnRevived() => session.ClearJobAppliedCache();

    public void EnableForFate() => EnableActivity(CombatActivity.Fate);

    public void EnableForCriticalEncounter() => EnableActivity(CombatActivity.CriticalEncounter);

    /// <summary>
    ///     Fight back while pot chest farming. The AI deliberately owns movement here: the farm does
    ///     not path during combat, so there is nothing to fight over — and the magic pot trails the
    ///     player, so letting the AI dodge takes the pot out of AoE with us. The pot can be
    ///     destroyed and the run lost with it, which is the real reason this matters (#188).
    /// </summary>
    public void EnableForSelfDefence() => EnableActivity(CombatActivity.Fate);

    /// <summary>
    ///     Drop combat automation while travelling. Normally a no-op inside a FATE/CE, since the
    ///     activity still wants the rotation — except when pot chest farming has taken over.
    /// </summary>
    public void DisableAi()
    {
        // Keep AI on only while In FATE / In CE owns the character (travel suspended).
        // A leftover CE EventId alone used to block Disable — Cursed Concern tagged travellers
        // and left RSR/BMR fighting trash while mounted (#200).
        if (!CombatSuppressedByActivity
            && memory.TryRemember<SuspendTravelForActivityMemory>(out SuspendTravelForActivityMemory _)
            && (criticalEncounters.IsInCriticalEncounter() || fates.IsInFate()))
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
        if (lastEnabledActivity != activity)
        {
            logger.Debug(
                "Combat AI Enable activity={Activity} recipe={Recipe}",
                activity,
                config.CombatAutorotation);
            lastEnabledActivity = activity;
            lastSyncSkipReason = null;
        }

        session.Enable(activity);
    }

    private void SyncActivityCombat()
    {
        // Without this the per-tick sync re-enables the rotation immediately: pot chest farming
        // usually runs while still standing in the pot FATE.
        if (CombatSuppressedByActivity)
        {
            LogSyncSkip("pot-farm");
            return;
        }

        // Pathfinding / CE wait own the character. Tick would re-Enable AutoTarget and pull
        // trash on the road (or at the registration rim) while we are still walking in.
        if (memory.TryRemember<GoalPathStepMemory>(out GoalPathStepMemory _)
            || memory.TryRemember<WaitingForCriticalEncounterMemory>(out WaitingForCriticalEncounterMemory _)
            || memory.TryRemember<WaitingForPotFateMemory>(out WaitingForPotFateMemory _))
        {
            LogSyncSkip("path-or-wait");
            return;
        }

        // EventId / IsInFate alone is not enough — only arm after In FATE / In CE entered
        // (SuspendTravel). Otherwise a CE you ride past keeps BOCCHI AI CE + RSR on (#200).
        if (!memory.TryRemember<SuspendTravelForActivityMemory>(out SuspendTravelForActivityMemory _))
        {
            LogSyncSkip("no-suspend-travel");
            return;
        }

        // Committed CE survives death (EventId can lag after YesAlready raise) — still re-arm.
        if (memory.TryRemember<CommittedCriticalEncounterMemory>(out CommittedCriticalEncounterMemory _)
            || criticalEncounters.IsInCriticalEncounter())
        {
            EnableActivity(CombatActivity.CriticalEncounter);
            return;
        }

        if (fates.IsInFate())
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

    private static CombatRotationRecipe ToRecipe(CombatAutorotation value) => value switch
    {
        CombatAutorotation.WrathCombo => new(
            JobRotationBackendKind.Wrath,
            CombatAiKind.MiscAi,
            ManualTargeting: true),
        CombatAutorotation.RotationSolverReborn => new(
            JobRotationBackendKind.RotationSolverReborn,
            CombatAiKind.MiscAi,
            ManualTargeting: true),
        CombatAutorotation.BossMod => new(JobRotationBackendKind.BossMod, CombatAiKind.None),
        CombatAutorotation.BossModReborn => new(JobRotationBackendKind.BossModReborn, CombatAiKind.None),
        _ => CombatRotationRecipe.None,
    };

    private bool ValidatePluginsForConfig()
    {
        switch (config.CombatAutorotation)
        {
            case CombatAutorotation.WrathCombo:
                if (!pluginStatus.IsLoaded(JobRotationBackendKeys.Wrath))
                {
                    PrintJobProviderMissing("Wrath Combo");
                    return false;
                }

                WarnIfBossModMissing();
                return true;

            case CombatAutorotation.RotationSolverReborn:
                if (!CombatPluginPresence.RotationSolverReborn(pluginStatus, rsr))
                {
                    PrintJobProviderMissing("Rotation Solver Reborn");
                    return false;
                }

                WarnIfBossModMissing();
                return true;

            case CombatAutorotation.BossMod:
                return ValidateBossModFork(
                    required: JobRotationBackendKeys.BossMod,
                    other: JobRotationBackendKeys.BossModReborn,
                    requiredLabel: "BossMod",
                    otherLabel: "BossMod Reborn");

            case CombatAutorotation.BossModReborn:
                return ValidateBossModFork(
                    required: JobRotationBackendKeys.BossModReborn,
                    other: JobRotationBackendKeys.BossMod,
                    requiredLabel: "BossMod Reborn",
                    otherLabel: "BossMod");

            default:
                return false;
        }
    }

    private bool ValidateBossModFork(string required, string other, string requiredLabel, string otherLabel)
    {
        if (pluginStatus.IsLoaded(required))
        {
            return true;
        }

        if (pluginStatus.IsLoaded(other))
        {
            BocchiChat.PrintError(
                chat,
                uiConfig,
                $"Combat autorotation is set to {requiredLabel}, but only {otherLabel} is loaded.");
        }
        else
        {
            PrintJobProviderMissing(requiredLabel);
        }

        return false;
    }

    private void WarnIfBossModMissing()
    {
        if (!pluginStatus.IsLoaded(JobRotationBackendKeys.BossMod)
            && !pluginStatus.IsLoaded(JobRotationBackendKeys.BossModReborn))
        {
            var job = player.GetClassJob();
            BocchiChat.PrintError(
                chat,
                uiConfig,
                $"BOCCHI AI / BossMod autorotation not ready (is BossMod / BMR loaded?). "
                + $"job={job?.Abbreviation.ToString() ?? "?"} melee={player.IsMelee()}");
        }
    }

    private uint? CurrentPhantomJobId() =>
        supportJobs.TryGetCurrent(out SupportJob current) ? current.Id.RowId() : null;

    private void PrintJobProviderMissing(string name)
    {
        BocchiChat.PrintError(
            chat,
            uiConfig,
            $"Combat autorotation needs {name}, but that plugin is not loaded.");
    }
}
