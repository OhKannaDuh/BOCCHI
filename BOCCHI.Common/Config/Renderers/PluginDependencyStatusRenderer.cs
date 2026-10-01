using System.Reflection;
using BOCCHI.Common.Config.Fields;
using BOCCHI.Common.Ipc.Knightshopper;
using BOCCHI.Common.Services;
using BOCCHI.Common.UI;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
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
    AutomatorConfig automator,
    DependencyPluginInstaller installer
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
        Draw("Travel (vnavmesh)", DependencyPlugins.VNavmesh, translator, VnavStatus);
        Draw("Lifestream", DependencyPlugins.Lifestream, translator, (_, t) => IpcStatus(lifestream.IsAvailable, t));

        ImGui.Spacing();
        BocchiUi.SectionTitle(T(translator, "shopping"));
        ImGui.Spacing();
        BocchiUi.MutedWrapped(T(translator, "shopping_intro"));
        ImGui.Spacing();
        Draw("Knightshopper", DependencyPlugins.Knightshopper, translator, (_, t) => IpcStatus(knightshopper.IsAvailable, t));

        ImGui.Spacing();
        BocchiUi.SectionTitle(T(translator, "optional"));
        ImGui.Spacing();
        if (automator.CombatAutorotation.UsesCombatAutomation())
        {
            BocchiUi.MutedWrapped(string.Format(T(translator, "using"), CombatDisplay.Display(automator.CombatAutorotation)));
        }

        BocchiUi.MutedWrapped(OptionalIntro(translator));
        ImGui.Spacing();

        Draw("Wrath Combo", DependencyPlugins.WrathCombo, translator, inUse: InUse("WrathCombo"));
        if (IsInstalled(CombatPluginPresence.RotationSolver))
        {
            Draw(
                "Rotation Solver Reborn",
                DependencyPlugins.RotationSolverReborn,
                translator,
                RsrIpcIfReachable,
                InUse(CombatPluginPresence.RotationSolver));
        }

        Draw("BossMod", DependencyPlugins.BossMod, translator, BossModIpcIfLoaded, InUse("BossMod"));
        if (IsInstalled("BossModReborn"))
        {
            Draw("BossMod Reborn", DependencyPlugins.BossModReborn, translator, BossModIpcIfLoaded, InUse("BossModReborn"));
        }

        return false;
    }

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

    private bool IsInstalled(string internalName) =>
        pluginStatus.IsInstalled(internalName)
        || (internalName == CombatPluginPresence.RotationSolver && rsr.IsAvailable);

    private bool InUse(string internalName) => automator.CombatAutorotation switch
    {
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
        DependencyPlugin dependency,
        ITranslator translator,
        Func<string, ITranslator, (string Label, bool Ok, bool Pending)>? ipc = null,
        bool inUse = false)
    {
        var (label, ok, pending) = ResolveStatus(dependency.InternalName, translator, ipc);
        if (inUse && ok)
        {
            label = $"{label} · {T(translator, "in_use")}";
        }

        ImGui.TextUnformatted(displayName);
        ImGui.SameLine(280f);
        BocchiUi.DrawStatusChip(label, StatusKind(ok, pending));

        if (!ok)
        {
            DrawActions(dependency, translator);
        }
    }

    private void DrawActions(DependencyPlugin dependency, ITranslator translator)
    {
        if (pluginStatus.IsLoaded(dependency.InternalName)
            || (dependency.InternalName == CombatPluginPresence.RotationSolver && rsr.IsAvailable))
        {
            return;
        }

        ImGui.PushID($"dependency_{dependency.InternalName}");
        if (plugin.InstalledPlugins.Any(p => p.InternalName == dependency.InternalName))
        {
            ImGui.SameLine();
            if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Plug, T(translator, "open_installer")))
            {
                installer.OpenInstaller(dependency);
            }

            ImGui.PopID();
            return;
        }

        if (!dependency.CanInstall)
        {
            ImGui.PopID();
            return;
        }

        bool busy = installer.IsInstalling(dependency);
        ImGui.BeginDisabled(busy);
        if (!installer.IsRepositoryAdded(dependency))
        {
            ImGui.SameLine();
            if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Plus, T(translator, "add_repo")))
            {
                installer.AddRepository(dependency, translator);
            }

            Tooltip(translator.T($"{StatusKey}.add_repo_tooltip", ("url", dependency.PrimaryRepositoryUrl)));
        }

        ImGui.SameLine();
        if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Download, T(translator, busy ? "installing" : "install")))
        {
            installer.Install(dependency, translator);
        }

        ImGui.EndDisabled();
        Tooltip(translator.T($"{StatusKey}.install_tooltip", ("plugin", dependency.DisplayName)));
        ImGui.PopID();
    }

    private static void Tooltip(string text)
    {
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
        {
            ImGui.SetTooltip(text);
        }
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
