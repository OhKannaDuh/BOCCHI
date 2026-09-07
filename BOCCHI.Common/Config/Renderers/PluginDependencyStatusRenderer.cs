using System.Reflection;
using BOCCHI.Common.Config.Fields;
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
    IRotationSolverRebornIpc rsr,
    AutomatorConfig automator
) : IFieldRenderer<PluginDependencyStatusAttribute>
{
    private const string StatusKey = "config.dependencies.fields.status";

    private static readonly CombatAutorotationDisplay CombatDisplay = new();

    public bool Render(object target, PropertyInfo prop, PluginDependencyStatusAttribute attr, Type owner, ITranslator translator)
    {
        BocchiUi.MutedWrapped(T(translator, "intro"));
        ImGui.Spacing();

        BocchiUi.SectionTitle(T(translator, "required"));
        ImGui.Spacing();
        Draw("vnavmesh", "vnavmesh", translator, VnavStatus);
        Draw("Lifestream", "Lifestream", translator, (_, t) => IpcStatus(lifestream.IsAvailable, t));

        ImGui.Spacing();
        BocchiUi.SectionTitle(T(translator, "shopping"));
        ImGui.Spacing();
        BocchiUi.MutedWrapped(T(translator, "shopping_intro"));
        ImGui.Spacing();
        Draw("Knightshopper", "Knightshopper", translator, (_, t) => IpcStatus(knightshopper.IsAvailable, t));

        ImGui.Spacing();
        BocchiUi.SectionTitle(T(translator, "optional"));
        ImGui.Spacing();
        if (automator.CombatAutorotation.UsesCombatAutomation())
        {
            BocchiUi.MutedWrapped(string.Format(T(translator, "using"), CombatDisplay.Display(automator.CombatAutorotation)));
        }

        BocchiUi.MutedWrapped(T(translator, "optional_intro"));
        ImGui.Spacing();

        Draw("Wrath Combo", "WrathCombo", translator, inUse: InUse("WrathCombo"));
        Draw(
            "Rotation Solver Reborn",
            CombatPluginPresence.RotationSolver,
            translator,
            RsrIpcIfReachable,
            InUse(CombatPluginPresence.RotationSolver));
        Draw("BossMod", "BossMod", translator, BossModIpcIfLoaded, InUse("BossMod"));
        Draw("BossMod Reborn", "BossModReborn", translator, BossModIpcIfLoaded, InUse("BossModReborn"));

        return false;
    }

    private bool InUse(string internalName) => automator.CombatAutorotation switch
    {
        CombatAutorotation.WrathCombo => internalName is "WrathCombo" or "BossMod" or "BossModReborn",
        CombatAutorotation.RotationSolverReborn =>
            internalName is CombatPluginPresence.RotationSolver or "BossMod" or "BossModReborn",
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
        var (label, ok, pending) = ResolveStatus(internalName, translator, ipc);
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

    private static BocchiUi.StatusChipKind StatusKind(bool ok, bool pending) =>
        pending ? BocchiUi.StatusChipKind.Warn : ok ? BocchiUi.StatusChipKind.Ok : BocchiUi.StatusChipKind.Muted;

    private static string T(ITranslator translator, string field) =>
        translator.T($"{StatusKey}.{field}");
}
