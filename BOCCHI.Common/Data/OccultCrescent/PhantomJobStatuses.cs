using BOCCHI.Common.Data.SupportJobs;
using Dalamud.Plugin.Services;
using Lumina.Excel;
using Lumina.Excel.Sheets;

namespace BOCCHI.Common.Data.OccultCrescent;

public static class PhantomJobStatuses
{
    private static readonly uint[] FallbackByJobIndex =
    [
        4242, 4358, 4359, 4360, 4361, 4362, 4363, 4364, 4365, 4366, 4367, 4368, 4369,
        4803, 4804, 4805,
        5328, 5329, 5330, 5331, 5332, 5333, 5334, 5335
    ];

    private static readonly uint[] Resolved = (uint[])FallbackByJobIndex.Clone();

    public static uint For(SupportJobId id)
    {
        int index = id.Index();
        if ((uint)index >= (uint)Resolved.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(id), id, null);
        }

        return Resolved[index];
    }

    public static void Initialize(IDataManager data)
    {
        ExcelSheet<MKDSupportJob> jobs = data.GetExcelSheet<MKDSupportJob>();
        ExcelSheet<Status> statuses = data.GetExcelSheet<Status>();

        Dictionary<string, uint> statusByName = new(StringComparer.Ordinal);
        foreach (Status row in statuses)
        {
            if (row.RowId == 0)
            {
                continue;
            }

            string name = row.Name.ToString().Trim();
            if (name.Length == 0)
            {
                continue;
            }

            bool occultRange = row.RowId is >= 4000 and < 6000;
            if (!statusByName.TryGetValue(name, out uint existing)
                || (occultRange && existing is < 4000 or >= 6000)
                || (row.IsPermanent && occultRange))
            {
                statusByName[name] = row.RowId;
            }
        }

        foreach (MKDSupportJob job in jobs)
        {
            if (job.RowId >= (uint)Resolved.Length)
            {
                continue;
            }

            string name = job.Name.ToString().Trim();
            if (name.Length > 0 && statusByName.TryGetValue(name, out uint statusId))
            {
                Resolved[job.RowId] = statusId;
            }
        }
    }
}
