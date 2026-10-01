using BOCCHI.Common.Config;
using BOCCHI.Common.UI;
using BOCCHI.Treasure.Hunt;
using BOCCHI.Treasure.Services;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using Ocelot.Services.Translation;
using Ocelot.Windows;

namespace BOCCHI.Treasure;

public static class TreasureHuntStatusUi
{
    public static string FormatProgress(ITreasureHunter hunter, ITranslator<MainWindow> translator)
    {
        if (hunter.WaitingForSafeWindow)
        {
            return translator.T(".treasure.waiting_safe_window");
        }

        int total = hunter.CheckedCofferCount + hunter.RemainingCofferCount;
        if (total <= 0)
        {
            return hunter.Paused ? translator.T(".treasure.paused") : string.Empty;
        }

        int current = hunter.CheckedCofferCount;
        if (hunter.GetCurrentStep()?.Type == HuntPathfinderStepType.WalkToNode)
        {
            current = Math.Min(current + 1, total);
        }

        string progress = $"{current}/{total}";
        if (hunter.Paused)
        {
            progress = $"{progress} ({translator.T(".treasure.paused")})";
        }

        return progress;
    }

    public static void DrawProgress(
        ITreasureHunter hunter,
        ITranslator<MainWindow> translator,
        TreasureConfig? config = null)
    {
        if (!hunter.Running)
        {
            return;
        }

        if (hunter.WaitingForSafeWindow)
        {
            BocchiUi.DrawStatusChip(
                translator.T(".treasure.waiting_safe_window"),
                BocchiUi.StatusChipKind.Warn);
            BocchiUi.MutedWrapped(translator.T(".treasure.waiting_safe_window_detail"));
            return;
        }

        int total = hunter.CheckedCofferCount + hunter.RemainingCofferCount;
        if (total <= 0 && hunter.StepCount <= 0)
        {
            return;
        }

        string progress = FormatProgress(hunter, translator);
        if (total > 0)
        {
            int current = hunter.CheckedCofferCount;
            if (hunter.GetCurrentStep()?.Type == HuntPathfinderStepType.WalkToNode)
            {
                current = Math.Min(current + 1, total);
            }

            float fraction = total > 0 ? current / (float)total : 0f;
            ImGui.TextColored(BocchiUi.Header, translator.T(".treasure.progress"));
            ImGui.SameLine(0f, 8f);
            if (hunter.Paused)
            {
                BocchiUi.DrawStatusChip(translator.T(".treasure.paused"), BocchiUi.StatusChipKind.Warn);
                ImGui.SameLine(0f, 8f);
            }

            BocchiUi.DrawPercentBar(
                fraction,
                Math.Min(220f, ImGui.GetContentRegionAvail().X),
                $"{current}/{total}");
        }
        else if (!string.IsNullOrEmpty(progress))
        {
            ImGui.TextColored(BocchiUi.Header, translator.T(".treasure.progress"));
            ImGui.SameLine(0f, 8f);
            BocchiUi.MutedText(progress);
        }

        if (hunter.LastCheckedNodeId is { })
        {
            ImGui.TextColored(BocchiUi.Header, translator.T(".treasure.last_checked"));
        }

        if (hunter.TryGetResumeCoffer(out uint _, out _))
        {
            ImGui.TextColored(BocchiUi.Header, translator.T(".treasure.resume_coffer"));
            ImGui.SameLine(0f, 8f);
            using (ImRaii.PushFont(UiBuilder.IconFont))
            {
                if (ImGui.SmallButton($"{FontAwesomeIcon.Flag.ToIconString()}##flag_hunt_resume"))
                {
                    hunter.FlagResumePoint();
                }
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(translator.T(".treasure.flag_resume_tooltip"));
            }

            ImGui.SameLine(0f, 8f);
            using (ImRaii.PushFont(UiBuilder.IconFont))
            {
                if (ImGui.SmallButton($"{FontAwesomeIcon.LocationArrow.ToIconString()}##recalculate_hunt_route"))
                {
                    hunter.RecalculateRoute();
                }
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(translator.T(".treasure.recalculate_route_tooltip"));
            }
        }

        HuntPathfinderStep? currentStep = hunter.GetCurrentStep();
        if (config != null && currentStep?.Type == HuntPathfinderStepType.WalkToNode)
        {
            ImGui.TextColored(BocchiUi.Header, translator.T(".treasure.distance_to_chest"));
            ImGui.SameLine(0f, 8f);
            BocchiUi.MutedText($"{hunter.StepDistance:F1}y");
        }
    }
}
