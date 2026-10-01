using BOCCHI.Common.Data.Goals;
using Dalamud.Plugin.Services;
using Lumina.Excel;
using Ocelot.Services.Translation;
using Ocelot.Windows;
using XIVDynamicEvent = Lumina.Excel.Sheets.DynamicEvent;
using XIVFate = Lumina.Excel.Sheets.Fate;

namespace BOCCHI.Common.UI;

public static class GoalFormatHelper
{
    public static string Describe(IGoal goal, ITranslator<MainWindow> translator, IDataManager data)
    {
        return goal.GoalType switch
        {
            FateGoal(var id) => string.Format(
                translator.T(".goals.fate"),
                FateName(data, id.Value)),
            CriticalEncounterGoal(var id) => string.Format(
                translator.T(".goals.critical_encounter"),
                CriticalEncounterName(data, id.Value)),
            ForkedTowerGoal(var id) => string.Format(
                translator.T(".goals.forked_tower"),
                CriticalEncounterName(data, id.Value)),
            var _ => goal.Describe()
        };
    }

    public static string FateName(IDataManager data, ushort fateId)
    {
        try
        {
            ExcelSheet<XIVFate> sheet = data.GetExcelSheet<XIVFate>();
            string name = sheet.GetRow(fateId).Name.ToString();
            return string.IsNullOrWhiteSpace(name) ? $"#{fateId}" : name;
        }
        catch
        {
            return $"#{fateId}";
        }
    }

    private static string CriticalEncounterName(IDataManager data, ushort id)
    {
        try
        {
            ExcelSheet<XIVDynamicEvent> sheet = data.GetExcelSheet<XIVDynamicEvent>();
            string name = sheet.GetRow(id).Name.ToString();
            return string.IsNullOrWhiteSpace(name) ? $"#{id}" : name;
        }
        catch
        {
            return $"#{id}";
        }
    }
}
