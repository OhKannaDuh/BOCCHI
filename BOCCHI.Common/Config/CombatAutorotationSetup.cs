using BOCCHI.Common;
using Dalamud.Plugin.Services;
using Ocelot.Ipc.RotationSolverReborn;
using Ocelot.Rotation.Services;
using Ocelot.Services.PluginStatus;

namespace BOCCHI.Common.Config;

public static class CombatAutorotationSetup
{
    public static CombatRotationRecipe ToRecipe(AutomatorConfig config) => config.CombatAutorotation switch
    {
        CombatAutorotation.WrathCombo => new(
            JobRotationBackendKind.Wrath,
            CombatAiKind.MiscAi,
            ManualTargeting: true,
            DisabledOccultOptions: [.. config.DisabledWrathOccultOptions]),
        CombatAutorotation.RotationSolverReborn => new(
            JobRotationBackendKind.RotationSolverReborn,
            CombatAiKind.MiscAi,
            ManualTargeting: true),
        CombatAutorotation.BossMod => new(JobRotationBackendKind.BossMod, CombatAiKind.None),
        CombatAutorotation.BossModReborn => new(JobRotationBackendKind.BossModReborn, CombatAiKind.None),
        _ => CombatRotationRecipe.None,
    };

    public static bool ValidatePlugins(
        CombatAutorotation value,
        IPluginStatus pluginStatus,
        IRotationSolverRebornIpc rsr,
        Action<string> printError)
    {
        switch (value)
        {
            case CombatAutorotation.WrathCombo:
                if (!pluginStatus.IsLoaded(JobRotationBackendKeys.Wrath))
                {
                    printError("Combat rotation needs Wrath Combo, but that plugin is not loaded.");
                    return false;
                }

                WarnIfBossModMissing(pluginStatus, printError);
                return true;

            case CombatAutorotation.RotationSolverReborn:
                if (!CombatPluginPresence.RotationSolverReborn(pluginStatus, rsr))
                {
                    printError("Combat rotation needs Rotation Solver Reborn, but that plugin is not loaded.");
                    return false;
                }

                WarnIfBossModMissing(pluginStatus, printError);
                return true;

            case CombatAutorotation.BossMod:
                return ValidateBossModFork(
                    pluginStatus,
                    printError,
                    required: JobRotationBackendKeys.BossMod,
                    other: JobRotationBackendKeys.BossModReborn,
                    requiredLabel: "BossMod",
                    otherLabel: "BossMod Reborn");

            case CombatAutorotation.BossModReborn:
                return ValidateBossModFork(
                    pluginStatus,
                    printError,
                    required: JobRotationBackendKeys.BossModReborn,
                    other: JobRotationBackendKeys.BossMod,
                    requiredLabel: "BossMod Reborn",
                    otherLabel: "BossMod");

            default:
                return false;
        }
    }

    public static void PrintError(IChatGui chat, UIConfig ui, string message) =>
        BocchiChat.PrintError(chat, ui, message);

    private static bool ValidateBossModFork(
        IPluginStatus pluginStatus,
        Action<string> printError,
        string required,
        string other,
        string requiredLabel,
        string otherLabel)
    {
        if (pluginStatus.IsLoaded(required))
        {
            return true;
        }

        if (pluginStatus.IsLoaded(other))
        {
            printError($"Combat rotation is set to {requiredLabel}, but only {otherLabel} is loaded.");
        }
        else
        {
            printError($"Combat rotation needs {requiredLabel}, but that plugin is not loaded.");
        }

        return false;
    }

    private static void WarnIfBossModMissing(IPluginStatus pluginStatus, Action<string> printError)
    {
        if (!pluginStatus.IsLoaded(JobRotationBackendKeys.BossMod)
            && !pluginStatus.IsLoaded(JobRotationBackendKeys.BossModReborn))
        {
            printError(
                "Combat rotation isn’t ready — load BossMod or BossMod Reborn, then pick it under General → Combat rotation.");
        }
    }
}
