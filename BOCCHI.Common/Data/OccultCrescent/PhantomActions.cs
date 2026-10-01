using BOCCHI.Common.Data.SupportJobs;
using Dalamud.Plugin.Services;
using Lumina.Excel;
using Lumina.Excel.Sheets;

namespace BOCCHI.Common.Data.OccultCrescent;

public static class PhantomActions
{
    public static uint BattleBell { get; private set; } = 41611;

    public static byte BattleBellUnlock { get; private set; } = 1;

    public static uint RingingRespite { get; private set; } = 41619;

    public static byte RingingRespiteUnlock { get; private set; } = 3;

    public static uint Revive { get; private set; } = 41634;

    public static uint OccultSprint { get; private set; } = 41646;

    public static uint OccultRaise { get; private set; } = 49070;

    public static uint OccultTreasuresight { get; private set; } = 41651;

    public static byte TreasuresightUnlockLevel { get; private set; } = 10;

    public static uint InquiringMind { get; private set; } = 46606;

    public static byte InquiringMindUnlock { get; private set; } = 15;

    public static uint Quickstep { get; private set; } = 46603;

    public static byte QuickstepUnlock { get; private set; } = 2;

    public static uint Pray { get; private set; } = 41589;

    public static byte PrayUnlock { get; private set; } = 2;

    public static uint RomeosBallad { get; private set; } = 41609;

    public static byte RomeosBalladUnlock { get; private set; } = 2;

    public static uint Counterstance { get; private set; } = 41597;

    public static byte CounterstanceUnlock { get; private set; } = 3;

    public static void Initialize(IDataManager data)
    {
        ExcelSheet<MKDSupportJob> jobs = data.GetExcelSheet<MKDSupportJob>();

        BattleBell = ReadAction(jobs, SupportJobId.PhantomGeomancer, 0, BattleBell);
        BattleBellUnlock = ReadUnlock(jobs, SupportJobId.PhantomGeomancer, 0, BattleBellUnlock);
        RingingRespite = ReadAction(jobs, SupportJobId.PhantomGeomancer, 2, RingingRespite);
        RingingRespiteUnlock = ReadUnlock(jobs, SupportJobId.PhantomGeomancer, 2, RingingRespiteUnlock);
        Revive = ReadAction(jobs, SupportJobId.PhantomChemist, 2, Revive);
        OccultRaise = ReadAction(jobs, SupportJobId.PhantomWhiteMage, 3, OccultRaise);
        OccultSprint = ReadAction(jobs, SupportJobId.PhantomThief, 0, OccultSprint);
        OccultTreasuresight = ReadAction(jobs, SupportJobId.PhantomFreelancer, 1, OccultTreasuresight);
        TreasuresightUnlockLevel = ReadUnlock(jobs, SupportJobId.PhantomFreelancer, 1, TreasuresightUnlockLevel);
        InquiringMind = ReadAction(jobs, SupportJobId.PhantomFreelancer, 2, InquiringMind);
        InquiringMindUnlock = ReadUnlock(jobs, SupportJobId.PhantomFreelancer, 2, InquiringMindUnlock);
        Quickstep = ReadAction(jobs, SupportJobId.PhantomDancer, 1, Quickstep);
        QuickstepUnlock = ReadUnlock(jobs, SupportJobId.PhantomDancer, 1, QuickstepUnlock);
        Pray = ReadAction(jobs, SupportJobId.PhantomKnight, 1, Pray);
        PrayUnlock = ReadUnlock(jobs, SupportJobId.PhantomKnight, 1, PrayUnlock);
        RomeosBallad = ReadAction(jobs, SupportJobId.PhantomBard, 1, RomeosBallad);
        RomeosBalladUnlock = ReadUnlock(jobs, SupportJobId.PhantomBard, 1, RomeosBalladUnlock);
        Counterstance = ReadAction(jobs, SupportJobId.PhantomMonk, 2, Counterstance);
        CounterstanceUnlock = ReadUnlock(jobs, SupportJobId.PhantomMonk, 2, CounterstanceUnlock);
    }

    private static uint ReadAction(
        ExcelSheet<MKDSupportJob> jobs,
        SupportJobId job,
        int slot,
        uint fallback)
    {
        if (!jobs.TryGetRow((uint)job, out MKDSupportJob row) || slot < 0 || slot >= row.Actions.Count)
        {
            return fallback;
        }

        uint id = row.Actions[slot].Action.RowId;
        return id == 0 ? fallback : id;
    }

    private static byte ReadUnlock(
        ExcelSheet<MKDSupportJob> jobs,
        SupportJobId job,
        int slot,
        byte fallback)
    {
        if (!jobs.TryGetRow((uint)job, out MKDSupportJob row) || slot < 0 || slot >= row.Actions.Count)
        {
            return fallback;
        }

        byte level = row.Actions[slot].LevelUnlock;
        return level == 0 ? fallback : level;
    }
}
