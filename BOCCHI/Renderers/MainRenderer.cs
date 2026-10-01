using BOCCHI.Common;
using BOCCHI.Common.Data.Zones;
using BOCCHI.Common.UI;
using BOCCHI.UI;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using Microsoft.Extensions.DependencyInjection;
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
    ITranslator<MainWindow> translator
) : IMainRenderer
{
    private IEnumerable<IDynamicRenderer>? renderers;

    private readonly HashSet<MainWindowSection> openedWhileActive = [];

    private IEnumerable<IDynamicRenderer> OrderedRenderers =>
        (renderers ??= services.GetServices<IDynamicRenderer>())
        .Where(r => r.ShouldRender())
        .OrderBy(r => r.Section)
        .ThenBy(r => r.Order);

    public void Render()
    {
        if (!zones.GetZone().IsOccultCrescentZone())
        {
            BocchiUi.DrawStatusChip(translator.T(".unsupported_zone"), BocchiUi.StatusChipKind.Warn);
            return;
        }

        statusBar.Render();
        BocchiUi.EndStickyHeader();

        using var body = ImRaii.Child("##bocchi_main_body", new Vector2(0, -1), false);
        if (!body.Success)
        {
            return;
        }

        MainWindowSection? expandRequest = statusBar.ExpandSectionRequest;
        if (expandRequest != null)
        {
            statusBar.ConsumeExpandRequest();
        }

        foreach (MainWindowSection section in Enum.GetValues<MainWindowSection>())
        {
            List<IDynamicRenderer> sectionRenderers = OrderedRenderers.Where(r => r.Section == section).ToList();
            if (sectionRenderers.Count == 0)
            {
                continue;
            }

            if (section == MainWindowSection.Trackers)
            {
                BocchiUi.SectionTitle(GetSectionTitle(section));
                ImGui.Spacing();
                if (BocchiUi.BeginPanel("trackers"))
                {
                    foreach (IDynamicRenderer renderer in sectionRenderers)
                    {
                        if (renderer.SubsectionTitle is { } title)
                        {
                            BocchiUi.MutedText(title);
                        }

                        renderer.Render();
                        ImGui.Spacing();
                    }

                    BocchiUi.EndPanel();
                }

                continue;
            }

            bool forceOpen = section switch
            {
                MainWindowSection.Automation => statusBar.IllegalModeActive,
                MainWindowSection.Completionist => statusBar.CompletionistActive,
                MainWindowSection.PotsTreasure => statusBar.PotsTreasureActive,
                MainWindowSection.MobFarmer => statusBar.MobFarmerActive,
                MainWindowSection.Treasure => statusBar.StandaloneTreasureHuntActive
                                             || statusBar.CarrotHuntActive,
                _ => false,
            };

            if (expandRequest == section)
            {
                ImGui.SetNextItemOpen(true);
            }
            else if (forceOpen)
            {
                if (openedWhileActive.Add(section))
                {
                    ImGui.SetNextItemOpen(true);
                }
            }
            else
            {
                openedWhileActive.Remove(section);
                ImGui.SetNextItemOpen(false, ImGuiCond.FirstUseEver);
            }

            ImGui.PushStyleColor(ImGuiCol.Text, BocchiUi.Header);
            bool open = ImGui.CollapsingHeader(GetSectionTitle(section));
            ImGui.PopStyleColor();

            if (!open)
            {
                continue;
            }

            ImGui.Indent();
            ImGui.Spacing();

            foreach (IDynamicRenderer renderer in sectionRenderers)
            {
                if (renderer.SubsectionTitle is { } title)
                {
                    BocchiUi.MutedText(title);
                    ImGui.Indent();
                }

                if (BocchiUi.BeginPanel($"{section}_{renderer.GetType().Name}"))
                {
                    renderer.Render();
                    BocchiUi.EndPanel();
                }

                if (renderer.SubsectionTitle != null)
                {
                    ImGui.Unindent();
                }

                ImGui.Spacing();
            }

            ImGui.Unindent();
        }
    }

    private string GetSectionTitle(MainWindowSection section) =>
        translator.T($".sections.{section.ToString().ToLowerInvariant()}");
}
