using BOCCHI.Common;
using BOCCHI.Common.Config;
using BOCCHI.Common.Services;
using BOCCHI.Common.UI;
using BOCCHI.Treasure.Services;
using Dalamud.Bindings.ImGui;
using Ocelot.Extensions;
using Ocelot.Services.PlayerState;
using Ocelot.Services.Translation;
using Ocelot.Windows;
using System.Numerics;

namespace BOCCHI.Treasure;

public class TreasureRenderer
(
    ITreasureTracker tracker,
    ITreasureHunter hunter,
    ICarrotHunter carrotHunter,
    IActivityNavigation navigation,
    TreasureConfig config,
    UIConfig uiConfig,
    IPlayer player,
    ITranslator<MainWindow> translator
) : IDynamicRenderer
{
    public MainWindowSection Section => MainWindowSection.Treasure;

    public void Render()
    {
        DrawActiveChests();
        DrawHuntPanel();
        DrawCarrotHuntPanel();
        DrawNearbyTreasures();
    }

    public bool ShouldRender() => uiConfig.ShowTreasureSection;

    private void DrawHuntPanel()
    {
        ImGui.Spacing();

        if (!hunter.IsVnavAvailable)
        {
            BocchiUi.DrawStatusChip(translator.T(".treasure.requires_vnav"), BocchiUi.StatusChipKind.Warn);
            return;
        }

        if (!hunter.IsVnavReady)
        {
            BocchiUi.DrawStatusChip(translator.T(".treasure.waiting_navmesh"), BocchiUi.StatusChipKind.Warn);
            return;
        }

        if (hunter.ManagedByPotsTreasure)
        {
            BocchiUi.MutedWrapped(translator.T(".treasure.managed_by_pots"));
            if (hunter.Elapsed > TimeSpan.Zero)
            {
                BocchiUi.LabelledValue(translator.T(".treasure.elapsed"), $"{hunter.Elapsed:mm\\:ss}");
            }

            TreasureHuntStatusUi.DrawProgress(hunter, translator, config);
            return;
        }

        if (hunter.ManagedByIllegalModeFiller)
        {
            BocchiUi.MutedWrapped(translator.T(".treasure.managed_by_illegal_mode"));
            if (hunter.Elapsed > TimeSpan.Zero)
            {
                BocchiUi.LabelledValue(translator.T(".treasure.elapsed"), $"{hunter.Elapsed:mm\\:ss}");
            }

            if (ImGui.Button(translator.T(".treasure.stop_hunt")))
            {
                hunter.Toggle();
            }

            TreasureHuntStatusUi.DrawProgress(hunter, translator, config);
            return;
        }

        if (hunter.ManagedByMobFarmer)
        {
            BocchiUi.MutedWrapped(translator.T(".treasure.managed_by_mob_farmer"));
            if (hunter.Elapsed > TimeSpan.Zero)
            {
                BocchiUi.LabelledValue(translator.T(".treasure.elapsed"), $"{hunter.Elapsed:mm\\:ss}");
            }

            TreasureHuntStatusUi.DrawProgress(hunter, translator, config);
            return;
        }

        if (carrotHunter.Running)
        {
            BocchiUi.MutedWrapped(translator.T(".treasure.managed_by_carrot"));
            return;
        }

        if (!hunter.Running)
        {
            if (ImGui.Button(translator.T(".treasure.start_hunt")))
            {
                hunter.Toggle();
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(translator.T(".treasure.start_hunt_tooltip"));
            }

            if (carrotHunter.IsVnavAvailable && carrotHunter.IsVnavReady)
            {
                ImGui.SameLine();
                if (ImGui.Button(translator.T(".treasure.start_carrot_hunt")))
                {
                    carrotHunter.Toggle();
                }

                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip(translator.T(".treasure.carrot_hunt_description"));
                }
            }

            return;
        }

        if (hunter.Paused)
        {
            if (ImGui.Button(translator.T(".treasure.resume_hunt")))
            {
                hunter.Resume();
            }
        }
        else if (ImGui.Button(translator.T(".treasure.pause_hunt")))
        {
            hunter.Pause();
        }

        ImGui.SameLine();
        if (ImGui.Button(translator.T(".treasure.stop_hunt")))
        {
            hunter.Toggle();
        }

        BocchiUi.LabelledValue(translator.T(".treasure.elapsed"), $"{hunter.Elapsed:mm\\:ss}");
        TreasureHuntStatusUi.DrawProgress(hunter, translator, config);
    }

    private void DrawCarrotHuntPanel()
    {
        if (hunter.Running || hunter.ManagedByPotsTreasure || hunter.ManagedByIllegalModeFiller || hunter.ManagedByMobFarmer)
        {
            return;
        }

        bool startsSharedWithHuntRow = !carrotHunter.Running
                                       && hunter.IsVnavAvailable
                                       && hunter.IsVnavReady
                                       && carrotHunter.IsVnavAvailable
                                       && carrotHunter.IsVnavReady;

        bool showCarrotStatus = carrotHunter.Running || carrotHunter.Elapsed > TimeSpan.Zero;
        bool showUseCarrot = showCarrotStatus || carrotHunter.FortuneCarrotsRemaining > 0;

        if (startsSharedWithHuntRow && !showCarrotStatus && !showUseCarrot)
        {
            return;
        }

        ImGui.Separator();
        BocchiUi.SectionTitle(translator.T(".treasure.carrot_hunt_title"));
        if (!carrotHunter.Running)
        {
            BocchiUi.DrawIntro(translator.T(".treasure.carrot_hunt_description"));
        }

        if (!carrotHunter.IsVnavAvailable)
        {
            BocchiUi.DrawStatusChip(translator.T(".treasure.requires_vnav"), BocchiUi.StatusChipKind.Warn);
            return;
        }

        if (!carrotHunter.IsVnavReady)
        {
            BocchiUi.DrawStatusChip(translator.T(".treasure.waiting_navmesh"), BocchiUi.StatusChipKind.Warn);
            return;
        }

        if (carrotHunter.Running)
        {
            if (ImGui.Button(translator.T(".treasure.stop_carrot_hunt")))
            {
                carrotHunter.Toggle();
            }
        }
        else if (!startsSharedWithHuntRow
                 && ImGui.Button(translator.T(".treasure.start_carrot_hunt")))
        {
            carrotHunter.Toggle();
        }

        if (showCarrotStatus)
        {
            BocchiUi.LabelledValue(translator.T(".treasure.elapsed"), $"{carrotHunter.Elapsed:mm\\:ss}");
            BocchiUi.LabelledValue(
                translator.T(".treasure.carrot_hunt_phase"),
                translator.T($".treasure.carrot_hunt_phases.{carrotHunter.Phase.ToString().ToSnakeCase()}"));
            BocchiUi.LabelledValue(
                translator.T(".treasure.fortune_carrots"),
                carrotHunter.FortuneCarrotsRemaining.ToString());
        }

        if (showUseCarrot)
        {
            ImGui.BeginDisabled(carrotHunter.FortuneCarrotsRemaining <= 0);
            if (ImGui.Button(translator.T(".treasure.use_fortune_carrot")))
            {
                carrotHunter.UseFortuneCarrot();
            }

            ImGui.EndDisabled();
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            {
                ImGui.SetTooltip(translator.T(".treasure.use_fortune_carrot_tooltip"));
            }
        }
    }

    private void DrawNearbyTreasures()
    {
        if (!hunter.Running && !carrotHunter.Running)
        {
            return;
        }

        ImGui.Separator();
        BocchiUi.SectionTitle(translator.T(".treasure.nearby_title"));

        if (tracker.Treasures.Count <= 0)
        {
            BocchiUi.MutedText(translator.T(".treasure.none_nearby"));
            return;
        }

        List<TreasureCoffer> treasures = tracker.Treasures
            .Where(t => t.IsValid())
            .OrderBy(t => player.Position.Distance(t.GetPosition()))
            .ToList();

        using ImGuiSectionHelper.BoundedListScope list =
            ImGuiSectionHelper.BoundedList("##nearby_treasures", treasures.Count, maxHeight: 120f);
        if (!list.IsOpen)
        {
            return;
        }

        foreach (TreasureCoffer treasure in treasures)
        {
            Vector3 pos = treasure.GetPosition();
            string name = treasure.GetName();
            string details =
                string.Format(translator.T(".treasure.distance"), player.Position.Distance(pos));

            ActivitySnapshotRenderer.RenderCompactWithActions(
                navigation,
                name,
                details,
                pos,
                $"treasure_{treasure.Id}",
                includeTeleport: false);
        }
    }

    private void DrawActiveChests() =>
        ActiveTreasureCountUi.Draw(tracker, config, translator);
}
