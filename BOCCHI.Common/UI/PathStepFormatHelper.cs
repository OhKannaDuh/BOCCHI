using BOCCHI.Common.Data.Paths;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;
using Ocelot.Services.Translation;
using Ocelot.Windows;

namespace BOCCHI.Common.UI;

public static class PathStepFormatHelper
{
    public static string Describe(IPathStep step, ITranslator<MainWindow> translator, IDataManager data)
    {
        return step.PathStepData switch
        {
            Pathfind => translator.T(".status.path_step_walk"),
            Teleport(var placeNameId) => FormatTeleport(placeNameId, translator, data),
            Return => translator.T(".status.path_step_return"),
            _ => step.Describe()
        };
    }

    private static string FormatTeleport(uint placeNameId, ITranslator<MainWindow> translator, IDataManager data)
    {
        string? name = null;
        try
        {
            if (data.GetExcelSheet<PlaceName>().TryGetRow(placeNameId, out PlaceName row))
            {
                string singular = row.Name.ToString();
                if (!string.IsNullOrWhiteSpace(singular))
                {
                    name = singular;
                }
            }
        }
        catch
        {
        }

        return string.IsNullOrWhiteSpace(name)
            ? translator.T(".status.path_step_teleport")
            : string.Format(translator.T(".status.path_step_teleport_to"), name);
    }
}
