using BOCCHI.Common;
using BOCCHI.Common.Data;
using BOCCHI.Common.Config;
using BOCCHI.Common.Data.SupportJobs;
using BOCCHI.Common.UI;
using BOCCHI.Experience.Services;
using Dalamud.Bindings.ImGui;
using Ocelot.Services.Translation;
using Ocelot.Windows;

namespace BOCCHI.Experience;

public class ExperienceRenderer
(
    IExperienceTracker tracker,
    UIConfig uiConfig,
    ISupportJobFactory supportJobs,
    ITranslator<MainWindow> translator
) : IDynamicRenderer
{
    public MainWindowSection Section => MainWindowSection.Trackers;

    public string CompactText => string.Format(
        translator.T(".trackers.experience.compact"),
        tracker.ExperiencePerHour.ToString("N0"));

    public void Render()
    {
        if (!supportJobs.TryGetCurrent(out SupportJob current))
        {
            ImGui.TextColored(BocchiUi.Bad, translator.T(".trackers.experience.no_job"));
            return;
        }

        BocchiUi.LabelledValue(translator.T(".trackers.experience.current_job"), current.Data.Name.ToString());

        BocchiUi.MutedText(
            string.Format(translator.T(".trackers.experience.level"), current.Level, current.TotalExperience));

        TrackerRateRenderer.RenderPerHour(
            translator.T(".trackers.experience.per_hour"),
            tracker.ExperiencePerHour,
            uiConfig.ShowTrackerGraphs ? tracker.GetExperienceHistory(DeltaRateTracker.DefaultGraphBucket) : [],
            "##xp_history",
            uiConfig.ShowTrackerGraphs
        );
    }

    public bool ShouldRender() => uiConfig.ShowExperienceTracker;
}
