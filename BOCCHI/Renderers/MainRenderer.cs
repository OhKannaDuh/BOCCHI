using BOCCHI.Common;
using BOCCHI.Common.Config;
using BOCCHI.Common.Data.Zones;
using BOCCHI.Common.UI;
using BOCCHI.UI;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using Microsoft.Extensions.DependencyInjection;
using Ocelot.Config;
using Ocelot.Extensions;
using Ocelot.Ipc.Lifestream;
using Ocelot.Ipc.RotationSolverReborn;
using Ocelot.Ipc.VNavmesh;
using Ocelot.Rotation.Services;
using Ocelot.Services.PluginStatus;
using Ocelot.Services.Translation;
using Ocelot.Services.WindowManager;
using Ocelot.Windows;
using System.Numerics;

namespace BOCCHI.Renderers;

public class MainRenderer
(
    IServiceProvider services,
    IZoneProvider zones,
    OperationalStatusBar statusBar,
    IConfigWindow configWindow,
    IVNavmeshIpc vnav,
    ILifestreamIpc lifestream,
    IPluginStatus pluginStatus,
    IRotationSolverRebornIpc rsr,
    AutomatorConfig automatorConfig,
    IConfigRenderer configRenderer,
    ITranslator<MainWindow> translator
) : IMainRenderer
{
    private static readonly (MainWindowSection Tab, MainWindowSection[] Sections, string ConfigGroup)[] Tabs =
    [
        (MainWindowSection.Automation, [MainWindowSection.Automation, MainWindowSection.Completionist], "automation"),
        (MainWindowSection.PotsTreasure, [MainWindowSection.PotsTreasure], "treasure"),
        (MainWindowSection.MobFarmer, [MainWindowSection.MobFarmer], "mob_farmer"),
        (MainWindowSection.Treasure, [MainWindowSection.Treasure], "treasure"),
        (MainWindowSection.World, [MainWindowSection.World], "fates"),
    ];

    private IEnumerable<IDynamicRenderer>? renderers;

    private readonly HashSet<MainWindowSection> activeSections = [];

    private MainWindowSection? pendingTab;

    private bool showCompletionist;

    public string? ConfigGroup { get; private set; }

    private List<IDynamicRenderer> OrderedRenderers =>
        (renderers ??= services.GetServices<IDynamicRenderer>())
        .Where(r => r.ShouldRender())
        .OrderBy(r => r.Section)
        .ThenBy(r => r.Order)
        .ToList();

    public void Render()
    {
        ConfigGroup = null;

        if (!zones.GetZone().IsOccultCrescentZone())
        {
            BocchiUi.DrawStatusChip(translator.T(".unsupported_zone"), BocchiUi.StatusChipKind.Warn);
            return;
        }

        List<IDynamicRenderer> all = OrderedRenderers;

        statusBar.Render();
        DrawTrackerLine(all.Where(r => r.Section == MainWindowSection.Trackers).ToList());
        BocchiUi.EndStickyHeader();

        FollowRunningMode();

        using var tabBar = ImRaii.TabBar("##bocchi_main_tabs");
        if (!tabBar.Success)
        {
            return;
        }

        foreach ((MainWindowSection tab, MainWindowSection[] sections, string configGroup) in Tabs)
        {
            List<IDynamicRenderer> tabRenderers = all.Where(r => sections.Contains(r.Section)).ToList();
            if (tabRenderers.Count == 0)
            {
                continue;
            }

            ImGuiTabItemFlags flags = pendingTab == tab ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None;
            using var item = ImRaii.TabItem($"{GetSectionTitle(tab)}###tab_{tab}", flags);
            if (pendingTab == tab)
            {
                pendingTab = null;
            }

            if (!item.Success)
            {
                continue;
            }

            using var body = ImRaii.Child($"##bocchi_tab_body_{tab}", new Vector2(0, -1), false);
            if (!body.Success)
            {
                continue;
            }

            ConfigGroup = configGroup;
            DrawTabBody(tab, tabRenderers);
        }

        // A hidden tab can't be selected; don't keep the request for a later frame.
        pendingTab = null;
    }

    private void FollowRunningMode()
    {
        if (statusBar.ExpandSectionRequest is { } request)
        {
            statusBar.ConsumeExpandRequest();
            Select(request);
        }

        Follow(MainWindowSection.Completionist, statusBar.CompletionistActive);
        Follow(MainWindowSection.Automation, statusBar.IllegalModeActive && !statusBar.CompletionistActive);
        Follow(MainWindowSection.PotsTreasure, statusBar.PotsTreasureActive);
        Follow(MainWindowSection.MobFarmer, statusBar.MobFarmerActive);
        bool wasHunting = activeSections.Contains(MainWindowSection.Treasure);
        Follow(MainWindowSection.Treasure, statusBar.HuntMoving);
        if (wasHunting && !statusBar.HuntMoving && statusBar.IllegalModeActive)
        {
            Select(statusBar.CompletionistActive ? MainWindowSection.Completionist : MainWindowSection.Automation);
        }
    }

    private void Follow(MainWindowSection section, bool active)
    {
        if (!active)
        {
            activeSections.Remove(section);
            return;
        }

        if (activeSections.Add(section))
        {
            Select(section);
        }
    }

    private void Select(MainWindowSection section)
    {
        if (section is MainWindowSection.Automation or MainWindowSection.Completionist)
        {
            showCompletionist = section == MainWindowSection.Completionist;
            pendingTab = MainWindowSection.Automation;
            return;
        }

        pendingTab = section;
    }

    private void DrawTabBody(MainWindowSection tab, List<IDynamicRenderer> tabRenderers)
    {
        ImGui.Spacing();

        if (tab == MainWindowSection.Automation)
        {
            bool hasNormal = tabRenderers.Any(r => r.Section == MainWindowSection.Automation);
            bool hasCompletionist = tabRenderers.Any(r => r.Section == MainWindowSection.Completionist);
            if (hasNormal && hasCompletionist)
            {
                if (ImGui.RadioButton(translator.T(".sections.automation_normal"), !showCompletionist))
                {
                    showCompletionist = false;
                }

                ImGui.SameLine(0f, 16f);
                if (ImGui.RadioButton(GetSectionTitle(MainWindowSection.Completionist), showCompletionist))
                {
                    showCompletionist = true;
                }
            }

            MainWindowSection shown = hasCompletionist && (showCompletionist || !hasNormal)
                ? MainWindowSection.Completionist
                : MainWindowSection.Automation;
            tabRenderers = tabRenderers.Where(r => r.Section == shown).ToList();
        }

        ImGui.Spacing();
        DrawMissingPlugin(tab);

        foreach (IDynamicRenderer renderer in tabRenderers)
        {
            if (renderer.SubsectionTitle is { } title)
            {
                BocchiUi.SectionTitle(title);
            }

            if (BocchiUi.BeginPanel($"{renderer.Section}_{renderer.GetType().Name}"))
            {
                renderer.Render();
                BocchiUi.EndPanel();
            }

            ImGui.Spacing();
        }

        DrawQuickSettings(tab);
    }

    private void DrawQuickSettings(MainWindowSection tab)
    {
        if (!QuickSettings.Tabs.TryGetValue(tab, out (Type Config, string Property)[]? fields)
            || !ImGui.CollapsingHeader($"{translator.T(".quick_settings")}###quick_settings_{tab}", ImGuiTreeNodeFlags.DefaultOpen))
        {
            return;
        }

        if (!BocchiUi.BeginPanel($"quick_settings_{tab}"))
        {
            return;
        }

        // Sliders default to a share of the window width, which pushes long labels past the panel edge.
        ITranslator root = services.GetRequiredService<ITranslator>();
        float labelWidth = fields.Max(f => f.Config.GetProperty(f.Property) is { } p ? ImGui.CalcTextSize(p.Label(f.Config, root)).X : 0f);
        float available = ImGui.GetContentRegionAvail().X;
        ImGui.PushItemWidth(MathF.Max(available * 0.35f, available - labelWidth - ImGui.GetStyle().ItemInnerSpacing.X - 24f));

        foreach ((Type configType, string property) in fields)
        {
            if (services.GetService(configType) is IAutoConfig config)
            {
                configRenderer.RenderField(config, property);
            }
        }

        ImGui.PopItemWidth();
        BocchiUi.EndPanel();
    }

    private void DrawMissingPlugin(MainWindowSection tab)
    {
        if (tab == MainWindowSection.World || MissingPlugin(tab) is not { } missing)
        {
            return;
        }

        BocchiUi.DrawStatusChip(string.Format(translator.T(".status.plugin_missing"), missing), BocchiUi.StatusChipKind.Warn);
        ImGui.SameLine(0f, 8f);
        if (ImGui.SmallButton(translator.T(".status.open_dependencies")))
        {
            configWindow.OpenGroup("dependencies");
        }

        ImGui.Spacing();
    }

    private string? MissingPlugin(MainWindowSection tab)
    {
        if (!vnav.IsAvailable())
        {
            return "vnavmesh";
        }

        if (!lifestream.IsAvailable)
        {
            return "Lifestream";
        }

        if (tab is not (MainWindowSection.Automation or MainWindowSection.MobFarmer))
        {
            return null;
        }

        return automatorConfig.CombatAutorotation switch
        {
            CombatAutorotation.WrathCombo when !pluginStatus.IsLoaded(JobRotationBackendKeys.Wrath) => "Wrath Combo",
            CombatAutorotation.RotationSolverReborn when !CombatPluginPresence.RotationSolverReborn(pluginStatus, rsr) => "Rotation Solver Reborn",
            CombatAutorotation.BossMod when !pluginStatus.IsLoaded(JobRotationBackendKeys.BossMod) => "BossMod",
            CombatAutorotation.BossModReborn when !pluginStatus.IsLoaded(JobRotationBackendKeys.BossModReborn) => "BossMod Reborn",
            _ => null,
        };
    }

    private void DrawTrackerLine(List<IDynamicRenderer> trackers)
    {
        if (trackers.Count == 0)
        {
            return;
        }

        string line = string.Join("  ·  ", trackers.Select(t => t.CompactText).Where(t => !string.IsNullOrEmpty(t)));
        if (line.Length == 0)
        {
            return;
        }

        ImGui.Spacing();
        BocchiUi.MutedText(line);
        if (!ImGui.IsItemHovered())
        {
            return;
        }

        using var tooltip = ImRaii.Tooltip();
        ImGui.Dummy(new Vector2(ImGui.GetFontSize() * 16f, 0f));
        foreach (IDynamicRenderer tracker in trackers)
        {
            tracker.Render();
            ImGui.Spacing();
        }
    }

    private string GetSectionTitle(MainWindowSection section) =>
        translator.T($".sections.{section.ToString().ToLowerInvariant()}");
}
