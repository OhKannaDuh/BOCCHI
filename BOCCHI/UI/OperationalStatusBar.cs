using BOCCHI.Automator.Data;
using BOCCHI.Automator.Services;
using BOCCHI.Buff.Services;
using BOCCHI.Common;
using BOCCHI.Common.Config;
using BOCCHI.Common.Data.StateMemory;
using BOCCHI.Common.Data.Zones;
using BOCCHI.Common.Services;
using BOCCHI.Common.UI;
using BOCCHI.MobFarmer.Data;
using BOCCHI.MobFarmer.Services;
using BOCCHI.Treasure;
using BOCCHI.Treasure.Services;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin.Services;
using Ocelot.Extensions;
using Ocelot.Services.Translation;
using Ocelot.Windows;

namespace BOCCHI.UI;

public class OperationalStatusBar
(
    Func<IAutomator> automatorFactory,
    Func<IPotsTreasureMode> potsTreasureFactory,
    Func<IMobFarmer> farmerFactory,
    ITreasureHunter hunter,
    ICarrotHunter carrotHunter,
    IBuffRunner buffRunner,
    UIConfig uiConfig,
    IAutomatorMemory memory,
    IPotCycleTracker potCycle,
    IZoneProvider zones,
    IDataManager data,
    ITranslator<MainWindow> translator
)
{
    private IAutomator? automator;

    private IPotsTreasureMode? potsTreasure;

    private IMobFarmer? farmer;

    private IAutomator Automator => automator ??= automatorFactory();

    private IPotsTreasureMode PotsTreasure => potsTreasure ??= potsTreasureFactory();

    private IMobFarmer Farmer => farmer ??= farmerFactory();

    /// <summary>Set when a status chip is clicked; MainRenderer opens that section once.</summary>
    public MainWindowSection? ExpandSectionRequest { get; private set; }

    public void ConsumeExpandRequest() => ExpandSectionRequest = null;

    public bool IllegalModeActive => Automator.Enabled;

    public bool CompletionistActive => Automator.IsCompletionist;

    public bool PotsTreasureActive => PotsTreasure.Running;

    public bool MobFarmerActive => Farmer.Running;

    public bool TreasureHuntActive => hunter.Running;

    public bool StandaloneTreasureHuntActive => hunter.Running && !hunter.ManagedByPotsTreasure;

    public bool CarrotHuntActive => carrotHunter.Running;

    public void Render()
    {
        bool shopping =
            Automator.SuspendedForShopping
            || (Farmer.Running && Farmer.Suspended && Farmer.YieldReason == FarmerYieldReason.Shopping);

        bool anyMode = IllegalModeActive || CompletionistActive || PotsTreasureActive || MobFarmerActive
                       || StandaloneTreasureHuntActive || CarrotHuntActive || shopping;

        List<string> runningParts = [];

        if (!anyMode)
        {
            BocchiUi.DrawStatusChip(translator.T(".status.idle"), BocchiUi.StatusChipKind.Muted);
        }
        else
        {
            bool first = true;
            void Chip(
                string label,
                string? detail,
                MainWindowSection section,
                BocchiUi.StatusChipKind kind = BocchiUi.StatusChipKind.Ok)
            {
                if (!first)
                {
                    ImGui.SameLine(0, 6);
                }

                first = false;
                string text = string.IsNullOrEmpty(detail) ? label : $"{label}: {detail}";
                if (BocchiUi.DrawStatusChip(text, kind))
                {
                    ExpandSectionRequest = section;
                }

                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip(translator.T(".status.click_section"));
                }

                runningParts.Add(string.IsNullOrEmpty(detail) ? label : $"{label} → {detail}");
            }

            if (IllegalModeActive)
            {
                Chip(
                    translator.T(".status.automator"),
                    Automator.CurrentState is { } state ? FormatAutomatorState(state) : translator.T(".status.on"),
                    MainWindowSection.Automation);
            }

            if (CompletionistActive)
            {
                Chip(
                    translator.T(".status.completionist"),
                    Automator.CurrentState is { } state ? FormatAutomatorState(state) : translator.T(".status.on"),
                    MainWindowSection.Completionist);
            }

            if (PotsTreasureActive)
            {
                string phase = translator.T(
                    $".automation.pots_treasure.phases.{PotsTreasure.Phase.ToString().ToSnakeCase()}");
                string? detail = PotsTreasure.Paused
                    ? translator.T(".automation.pots_treasure.paused")
                    : phase;
                if (!PotsTreasure.Paused
                    && PotsTreasure.Phase == PotsTreasurePhase.Hunting
                    && hunter.Running
                    && (hunter.StepCount > 0 || hunter.WaitingForSafeWindow))
                {
                    detail = $"{phase} · {TreasureHuntStatusUi.FormatProgress(hunter, translator)}";
                }

                Chip(
                    translator.T(".status.pots_treasure"),
                    detail,
                    MainWindowSection.PotsTreasure,
                    PotsTreasure.Paused ? BocchiUi.StatusChipKind.Warn : BocchiUi.StatusChipKind.Ok);
            }

            if (MobFarmerActive)
            {
                string detail = Farmer.Suspended
                    ? translator.T($".automation.mob_farmer.yield_reasons.{Farmer.YieldReason.ToString().ToSnakeCase()}")
                    : translator.T($"state_machines.farmer_phase.{Farmer.Phase.ToString().ToSnakeCase()}.label");
                if (!Farmer.Suspended && Farmer.CurrentSpotName is { } spot)
                {
                    detail = $"{detail} · {spot}";
                }

                Chip(
                    translator.T(".status.mob_farmer"),
                    detail,
                    MainWindowSection.MobFarmer,
                    Farmer.Suspended ? BocchiUi.StatusChipKind.Warn : BocchiUi.StatusChipKind.Ok);
            }

            if (StandaloneTreasureHuntActive)
            {
                Chip(
                    translator.T(".status.treasure_hunt"),
                    TreasureHuntStatusUi.FormatProgress(hunter, translator),
                    MainWindowSection.Treasure);
            }

            if (CarrotHuntActive)
            {
                Chip(
                    translator.T(".status.carrot_hunt"),
                    translator.T($".treasure.carrot_hunt_phases.{carrotHunter.Phase.ToString().ToSnakeCase()}"),
                    MainWindowSection.Treasure);
            }

            if (shopping)
            {
                if (!first)
                {
                    ImGui.SameLine(0, 6);
                }

                first = false;
                BocchiUi.DrawStatusChip(translator.T(".status.shopping_paused"), BocchiUi.StatusChipKind.Warn);
                runningParts.Add(translator.T(".status.shopping_paused"));
            }
        }

        string? potChip = PotTimerUi.FormatCompact(potCycle, data, translator);
        if (potChip != null)
        {
            ImGui.SameLine(0f, 10f);
            BocchiUi.DrawStatusChip(potChip, BocchiUi.StatusChipKind.Muted);
        }

        // Ready path-map stays quiet — only show while loading/building.
        if (ZoneGraphStatusUi.TryFormat(
                zones.GetZone(),
                translator,
                out _,
                out string pathMapValue,
                out bool pathMapBusy)
            && pathMapBusy)
        {
            ImGui.SameLine(0f, 10f);
            BocchiUi.DrawStatusChip(
                $"{translator.T(".automation.automator.path_map")}: {pathMapValue}",
                BocchiUi.StatusChipKind.Warn);
        }

        if (uiConfig.ShowBuffSection)
        {
            ImGui.SameLine(0f, 10f);
            DrawBuffAction();
        }

        if (runningParts.Count > 0)
        {
            ImGui.Spacing();
            BocchiUi.MutedText($"{translator.T(".status.whats_running")}: {string.Join(" · ", runningParts)}");
        }

        bool showGoalRows = IllegalModeActive
                            || CompletionistActive
                            || (PotsTreasureActive && PotsTreasure.Phase == PotsTreasurePhase.DoingPots);
        if (showGoalRows)
        {
            ImGui.Spacing();
            if (memory.TryRemember<GoalMemory>(out GoalMemory goalMemory))
            {
                BocchiUi.MutedText($"{translator.T(".status.goal")}: {GoalFormatHelper.Describe(goalMemory.Goal, translator)}");
            }

            if (memory.TryRemember<PotChestFarmMemory>(out PotChestFarmMemory potFarm))
            {
                BocchiUi.MutedText(
                    $"{translator.T(".status.chests")}: {potFarm.RemainingChests}/{potFarm.TotalChests} (Fate {potFarm.FateId.Value})");
            }
        }
    }

    private void DrawBuffAction()
    {
        bool canStart = buffRunner.CanStart;
        bool running = buffRunner.IsRunning;
        string label = running
            ? translator.T(".buffs.stop_button")
            : translator.T(".buffs.apply_button");

        using (ImRaii.Disabled(!canStart && !running))
        {
            using (ImRaii.PushFont(UiBuilder.IconFont))
            {
                ImGui.AlignTextToFramePadding();
                ImGui.TextUnformatted(FontAwesomeIcon.Flask.ToIconString());
            }

            ImGui.SameLine(0f, 6f);
            if (ImGui.SmallButton($"{label}##buffs_action"))
            {
                if (running)
                {
                    buffRunner.Stop();
                }
                else
                {
                    buffRunner.Start();
                }
            }
        }

        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
        {
            if (running)
            {
                ImGui.SetTooltip(translator.T(".buffs.stop_tooltip"));
            }
            else if (canStart)
            {
                ImGui.SetTooltip(translator.T(".buffs.apply_tooltip"));
            }
            else
            {
                ImGui.SetTooltip(buffRunner.DisabledReason ?? translator.T(".buffs.apply_tooltip"));
            }
        }

        if (running)
        {
            ImGui.SameLine(0f, 8f);
            BocchiUi.DrawStatusChip(translator.T(".buffs.applying"), BocchiUi.StatusChipKind.Warn);
        }
    }

    private string FormatAutomatorState(AutomatorState state) =>
        translator.T($"state_machines.automator.{state.ToString().ToSnakeCase()}.label");
}
