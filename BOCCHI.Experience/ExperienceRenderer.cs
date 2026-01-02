using System.Numerics;
using BOCCHI.Common;
using BOCCHI.Common.Config;
using BOCCHI.Common.Data.SupportJobs;
using BOCCHI.Experience.Services;
using Dalamud.Bindings.ImGui;
using Ocelot.Services.UI;

namespace BOCCHI.Experience;

public class ExperienceRenderer(
    IExperienceTracker tracker,
    ExperienceConfig config,
    UIConfig uiConfig,
    ISupportJobFactory supportJobs,
    IBrandingService branding,
    IUIService ui
) : IDynamicRenderer
{
    public void Render()
    {
        if (!supportJobs.TryGetCurrent(out var current))
        {
            ui.Text("No jobs found", branding.DalamudRed);
            return;
        }

        var left = ui.Compose()
            .Text("Current Job", branding.DalamudYellow)
            .Text(current.Id.ToString());

        var right = ui.Compose()
            .Text($"Level: {current.Level} ({current.TotalExperience})");

        ui.Render(left, right);

        ui.LabelledValue("Experience Per Hour", tracker.ExperiencePerHour.ToString("f2"));

        if (!uiConfig.ShowExperienceTrackerGraph)
        {
            return;
        }

        var history = tracker.GetExperienceHistory(TimeSpan.FromSeconds(config.GraphBucketSize));

        if (history.Length <= 0)
        {
            return;
        }

        var max = history.Max();
        if (max <= 0f)
        {
            max = 1f;
        }

        var size = new Vector2(ImGui.GetContentRegionAvail().X, 30);

        ImGui.PlotLines(
            "##xp_history",
            history.AsSpan(),
            history.Length,
            string.Empty,
            0f,
            max,
            size,
            sizeof(float)
        );
    }

    public bool ShouldRender()
    {
        return uiConfig.ShowExperienceTracker;
    }
}
