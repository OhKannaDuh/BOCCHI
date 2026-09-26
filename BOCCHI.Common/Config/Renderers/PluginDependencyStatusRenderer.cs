using System.Reflection;
using BOCCHI.Common.Config.Fields;
using BOCCHI.Common.Ipc.GatherBuddy;
using BOCCHI.Common.Ipc.Knightshopper;
using BOCCHI.Common.UI;
using Dalamud.Bindings.ImGui;
using Dalamud.Plugin;
using Ocelot.Config.Renderers;
using Ocelot.Ipc.BossMod;
using Ocelot.Ipc.Lifestream;
using Ocelot.Ipc.RotationSolverReborn;
using Ocelot.Ipc.VNavmesh;
using Ocelot.Services.PluginStatus;
using Ocelot.Services.Translation;

namespace BOCCHI.Common.Config.Renderers;

public sealed class PluginDependencyStatusRenderer(
    IDalamudPluginInterface plugin,
    IPluginStatus pluginStatus,
    IVNavmeshIpc vnav,
    IBossModIpc bossMod,
    ILifestreamIpc lifestream,
    IKnightshopperIpc knightshopper,
    IGatherBuddyIpc gatherBuddy,
    IRotationSolverRebornIpc rsr,
    AutomatorConfig automator,
    ShoppingConfig shopping
) : IFieldRenderer<PluginDependencyStatusAttribute>
{
    private const string StatusKey = "config.dependencies.fields.status";

    private static readonly CombatAutorotationDisplay CombatDisplay = new();

    /// <summary>Upstream manifest name first, then the BOCCHI-bld fork's.</summary>
    private static readonly string[] GatherBuddyInternalNames = ["GatherBuddyReborn", "GatherBuddyReborn-bld"];

    public bool Render(object target, PropertyInfo prop, PluginDependencyStatusAttribute attr, Type owner, ITranslator translator)
    {
        BocchiUi.MutedWrapped(T(translator, "intro"));
        ImGui.Spacing();

        BocchiUi.SectionTitle(T(translator, "required"));
        ImGui.Spacing();
        Draw("Travel (vnavmesh)", "vnavmesh", translator, VnavStatus);
        Draw("Lifestream", "Lifestream", translator, (_, t) => IpcStatus(lifestream.IsAvailable, t));

        ImGui.Spacing();
        BocchiUi.SectionTitle(T(translator, "shopping"));
        ImGui.Spacing();
        BocchiUi.MutedWrapped(T(translator, "shopping_intro"));
        ImGui.Spacing();
        DrawRow("GatherBuddy Reborn", GatherBuddyStatus(translator), translator, !shopping.UsesKnightshopper);
        Draw(
            "Knightshopper",
            "Knightshopper",
            translator,
            (_, t) => IpcStatus(knightshopper.IsAvailable, t),
            shopping.UsesKnightshopper);

        ImGui.Spacing();
        BocchiUi.SectionTitle(T(translator, "optional"));
        ImGui.Spacing();
        if (automator.CombatAutorotation.UsesCombatAutomation())
        {
            BocchiUi.MutedWrapped(string.Format(T(translator, "using"), CombatDisplay.Display(automator.CombatAutorotation)));
        }

        BocchiUi.MutedWrapped(OptionalIntro(translator));
        ImGui.Spacing();

        Draw("Wrath Combo", "WrathCombo", translator, inUse: InUse("WrathCombo"));
        if (ShouldShowOptional(CombatPluginPresence.RotationSolver, InUse(CombatPluginPresence.RotationSolver)))
        {
            Draw(
                "Rotation Solver Reborn",
                CombatPluginPresence.RotationSolver,
                translator,
                RsrIpcIfReachable,
                InUse(CombatPluginPresence.RotationSolver));
        }

        Draw("BossMod", "BossMod", translator, BossModIpcIfLoaded, InUse("BossMod"));
        if (ShouldShowOptional("BossModReborn", InUse("BossModReborn")))
        {
            Draw("BossMod Reborn", "BossModReborn", translator, BossModIpcIfLoaded, InUse("BossModReborn"));
        }

        return false;
    }

    /// <summary>
    ///     Mentions RSR / BossMod Reborn only when those plugins are installed — there is no
    ///     plain “Rotation Solver”, only Rotation Solver Reborn.
    /// </summary>
    private string OptionalIntro(ITranslator translator)
    {
        bool rsr = IsInstalled(CombatPluginPresence.RotationSolver);
        bool bmr = IsInstalled("BossModReborn");
        string key = (rsr, bmr) switch
        {
            (true, true) => "optional_intro_rsr_bmr",
            (true, false) => "optional_intro_rsr",
            (false, true) => "optional_intro_bmr",
            _ => "optional_intro",
        };
        return T(translator, key);
    }

    /// <summary>
    ///     RSR / BossMod Reborn are alternate forks — hide when not installed unless the player
    ///     already selected them (so a broken pick still explains itself).
    /// </summary>
    private bool ShouldShowOptional(string internalName, bool inUse) =>
        inUse || IsInstalled(internalName);

    private bool IsInstalled(string internalName) =>
        pluginStatus.IsInstalled(internalName)
        || (internalName == CombatPluginPresence.RotationSolver && rsr.IsAvailable);

    private bool InUse(string internalName) => automator.CombatAutorotation switch
    {
        // Wrath / RSR need a BossMod fork for BOCCHI AI — only mark forks that are actually installed.
        CombatAutorotation.WrathCombo => internalName == "WrathCombo"
            || ((internalName is "BossMod" or "BossModReborn") && IsInstalled(internalName)),
        CombatAutorotation.RotationSolverReborn =>
            internalName == CombatPluginPresence.RotationSolver
            || ((internalName is "BossMod" or "BossModReborn") && IsInstalled(internalName)),
        CombatAutorotation.BossMod => internalName == "BossMod",
        CombatAutorotation.BossModReborn => internalName == "BossModReborn",
        _ => false,
    };

    private (string Label, bool Ok, bool Pending) VnavStatus(string _, ITranslator translator)
    {
        if (!vnav.IsAvailable())
        {
            return (T(translator, "not_working"), false, false);
        }

        return vnav.IsNavmeshReady()
            ? (T(translator, "ready"), true, false)
            : (T(translator, "map_loading"), true, true);
    }

    private (string Label, bool Ok, bool Pending) RsrIpcIfReachable(string _, ITranslator translator) =>
        IpcStatus(rsr.IsAvailable, translator);

    private (string Label, bool Ok, bool Pending) BossModIpcIfLoaded(string _, ITranslator translator) =>
        IpcStatus(bossMod.IsAvailable, translator);

    private static (string Label, bool Ok, bool Pending) IpcStatus(bool available, ITranslator translator) =>
        available
            ? (T(translator, "ready"), true, false)
            : (T(translator, "not_working"), false, false);

    private void Draw(
        string displayName,
        string internalName,
        ITranslator translator,
        Func<string, ITranslator, (string Label, bool Ok, bool Pending)>? ipc = null,
        bool inUse = false)
    {
        DrawRow(displayName, ResolveStatus(internalName, translator, ipc), translator, inUse);
    }

    private static void DrawRow(
        string displayName,
        (string Label, bool Ok, bool Pending) status,
        ITranslator translator,
        bool inUse)
    {
        var (label, ok, pending) = status;
        if (inUse && ok)
        {
            label = $"{label} · {T(translator, "in_use")}";
        }

        ImGui.TextUnformatted(displayName);
        ImGui.SameLine(280f);
        BocchiUi.DrawStatusChip(label, StatusKind(ok, pending));
    }

    private (string Label, bool Ok, bool Pending) ResolveStatus(
        string internalName,
        ITranslator translator,
        Func<string, ITranslator, (string Label, bool Ok, bool Pending)>? ipc)
    {
        bool loaded = pluginStatus.IsLoaded(internalName);
        bool rsrReachable = internalName == CombatPluginPresence.RotationSolver && rsr.IsAvailable;
        if (!loaded && !rsrReachable)
        {
            if (plugin.InstalledPlugins.Any(p => p.InternalName == internalName))
            {
                return (T(translator, "not_enabled"), false, false);
            }

            return (T(translator, "not_installed"), false, false);
        }

        // Loaded plugin counts as Ready. Optional IPC probes (BossMod / RSR) can still refine,
        // but RSR must not show Not working when the plugin is running and only IPC typing failed.
        if (ipc == null)
        {
            return (T(translator, "ready"), true, false);
        }

        var (label, ok, pending) = ipc.Invoke(internalName, translator);
        if (!ok && loaded)
        {
            return (T(translator, "ready"), true, false);
        }

        return (label, ok, pending);
    }

    /// <summary>
    ///     GBR ships under more than one InternalName, and a loaded build can still predate the
    ///     vendor-list IPC — so IPC reachability decides Ready, not the plugin being loaded.
    /// </summary>
    private (string Label, bool Ok, bool Pending) GatherBuddyStatus(ITranslator translator)
    {
        if (gatherBuddy.IsAvailable)
        {
            return (T(translator, "ready"), true, false);
        }

        if (GatherBuddyInternalNames.Any(pluginStatus.IsLoaded))
        {
            return gatherBuddy.Version > 0
                ? (T(translator, "outdated"), false, true)
                : (T(translator, "not_working"), false, false);
        }

        return plugin.InstalledPlugins.Any(p => GatherBuddyInternalNames.Contains(p.InternalName))
            ? (T(translator, "not_enabled"), false, false)
            : (T(translator, "not_installed"), false, false);
    }

    private static BocchiUi.StatusChipKind StatusKind(bool ok, bool pending) =>
        pending ? BocchiUi.StatusChipKind.Warn : ok ? BocchiUi.StatusChipKind.Ok : BocchiUi.StatusChipKind.Muted;

    private static string T(ITranslator translator, string field) =>
        translator.T($"{StatusKey}.{field}");
}
