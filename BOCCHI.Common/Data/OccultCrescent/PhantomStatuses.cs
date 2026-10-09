using Dalamud.Game;
using Dalamud.Plugin.Services;
using Lumina.Excel;
using Lumina.Excel.Sheets;
using ExcelAction = Lumina.Excel.Sheets.Action;

namespace BOCCHI.Common.Data.OccultCrescent;

public static class PhantomBuffs
{
    public static ushort EnduringFortitude { get; private set; } = 4233;

    public static ushort Fleetfooted { get; private set; } = 4239;

    public static ushort RomeosBallad { get; private set; } = 4244;

    public static ushort BattleBell { get; private set; } = 4251;

    public static ushort BattlesClangor { get; private set; } = 4252;

    public static ushort RingingRespite { get; private set; } = 4253;

    public static ushort QuickerStep { get; private set; } = 4799;

    public static void Initialize(IDataManager data)
    {
        ExcelSheet<Status> statuses = data.GetExcelSheet<Status>();
        ExcelSheet<Status> statusesEn = data.GetExcelSheet<Status>(ClientLanguage.English);
        ExcelSheet<ExcelAction> actions = data.GetExcelSheet<ExcelAction>();

        BattleBell = FromActionName(actions, statuses, PhantomActions.BattleBell, BattleBell);
        RingingRespite = FromActionName(actions, statuses, PhantomActions.RingingRespite, RingingRespite);
        RomeosBallad = FromActionName(actions, statuses, PhantomActions.RomeosBallad, RomeosBallad);
        EnduringFortitude = FindByName(statusesEn, "Enduring Fortitude", EnduringFortitude);
        Fleetfooted = FindByName(statusesEn, "Fleetfooted", Fleetfooted);
        QuickerStep = FindByName(statusesEn, "Quicker Step", QuickerStep);
        BattlesClangor = FindByName(statusesEn, "Battle's Clangor", BattlesClangor);
    }

    private static ushort FromActionName(
        ExcelSheet<ExcelAction> actions,
        ExcelSheet<Status> statuses,
        uint actionId,
        ushort fallback)
    {
        if (!actions.TryGetRow(actionId, out ExcelAction action))
        {
            return fallback;
        }

        string name = action.Name.ToString().Trim();
        return name.Length == 0 ? fallback : FindByName(statuses, name, fallback);
    }

    private static ushort FindByName(ExcelSheet<Status> statuses, string name, ushort fallback)
    {
        ushort best = fallback;
        var found = false;
        foreach (Status row in statuses)
        {
            if (row.RowId == 0 || !row.Name.ToString().Trim().Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            bool occultRange = row.RowId is >= 4000 and < 6000;
            if (!found || occultRange)
            {
                best = (ushort)row.RowId;
                found = true;
                if (occultRange)
                {
                    return best;
                }
            }
        }

        return best;
    }
}

public static class PlayerStatuses
{
    public const ushort Raise = 148;
}
