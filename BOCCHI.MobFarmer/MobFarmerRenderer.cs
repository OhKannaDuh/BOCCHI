using BOCCHI.Common;
using BOCCHI.Common.Config;
using BOCCHI.Common.UI;
using BOCCHI.MobFarmer.Services;
using Dalamud.Bindings.ImGui;
using Ocelot.Extensions;
using Ocelot.Services.Translation;
using Ocelot.Windows;

namespace BOCCHI.MobFarmer;

public class MobFarmerRenderer
(
    Func<IMobFarmer> farmerFactory,
    IMobScanner scanner,
    MobFarmerPanelState panelState,
    MobFarmerConfig config,
    ITranslator<MainWindow> translator
) : IDynamicRenderer
{
    private IMobFarmer? farmer;

    private IMobFarmer Farmer => farmer ??= farmerFactory();

    public MainWindowSection Section => MainWindowSection.MobFarmer;

    public void Render()
    {
        panelState.MarkRendered();

        BocchiUi.DrawIntro(translator.T(".automation.mob_farmer.description"));

        if (ImGui.Button(Farmer.Running
            ? translator.T(".automation.mob_farmer.stop")
            : translator.T(".automation.mob_farmer.start")))
        {
            Farmer.Toggle();
        }

        ImGui.Spacing();
        BocchiUi.LabelledValue(translator.T(".automation.mob_farmer.not_engaged"), scanner.NotInCombat.Count.ToString());
        BocchiUi.LabelledValue(translator.T(".automation.mob_farmer.engaged"), scanner.InCombat.Count.ToString());
        BocchiUi.LabelledValue(translator.T(".automation.mob_farmer.selected_mobs"), config.Mobs.Count.ToString());
        if (Farmer.CurrentSpotName is { } spotName)
        {
            BocchiUi.LabelledValue(translator.T(".automation.mob_farmer.spot"), spotName);
        }

        BocchiUi.MutedText(translator.T(".automation.mob_farmer.configure_mobs"));

        if (Farmer.Running)
        {
            if (Farmer.Suspended)
            {
                BocchiUi.LabelledValue(
                    translator.T(".automation.mob_farmer.yield"),
                    translator.T($".automation.mob_farmer.yield_reasons.{Farmer.YieldReason.ToString().ToSnakeCase()}"));
            }

            Farmer.Render();
        }
    }

    public bool ShouldRender() => true;
}
