using System.Collections.Generic;
using Dalamud.Game.ClientState.Objects.Types;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game.InstanceContent;
using Lumina.Excel.Sheets;
using Ocelot.Ui;

namespace BOCCHI.Modules.Debug.Panels;

public class JobLevelPanel : Panel
{
    private List<IGameObject> enemies = [];

    public override string GetName()
    {
        return "Job Level";
    }

    public override unsafe void Render(DebugModule module)
    {
        // var level = PublicContentOccultCrescent.GetState()->SupportJobLevels[1];
        var state = PublicContentOccultCrescent.GetState();
        OcelotUi.Indent(() =>
        {
            foreach (var job in Svc.Data.GetExcelSheet<MKDSupportJob>())
            {
                OcelotUi.Title(job.Name.ToString());
                OcelotUi.Indent(() =>
                {
                    var level = state->SupportJobLevels[(byte)job.RowId];
                    OcelotUi.LabelledValue("Level", $"{level}/{job.LevelMax}");
                });

                OcelotUi.Indent(() =>
                {
                    var exp = state->SupportJobExperience[(byte)job.RowId];
                    OcelotUi.LabelledValue("Exp", exp);
                });
            }
        });
    }
}
