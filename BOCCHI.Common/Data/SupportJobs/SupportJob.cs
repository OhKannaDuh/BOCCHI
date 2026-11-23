using BOCCHI.Common.Data.OccultCrescent;
using FFXIVClientStructs.FFXIV.Client.Game.InstanceContent;
using Lumina.Excel;
using Lumina.Excel.Sheets;

namespace BOCCHI.Common.Data.SupportJobs;

public class SupportJob
{
    public SupportJobId Id { get; init; }

    public MKDSupportJob Data { get; init; }

    public SubrowCollection<MKDGrowDataSJob> GrowthData { get; init; }

    public unsafe byte Level
    {
        get
        {
            OccultCrescentState* state = PublicContentOccultCrescent.GetState();
            if (state == null || state->SupportJobLevels.Length <= Id.Index())
            {
                return 0;
            }

            return state->SupportJobLevels[Id.Index()];
        }
    }

    public unsafe uint CurrentExperience
    {
        get
        {
            OccultCrescentState* state = PublicContentOccultCrescent.GetState();
            if (state == null || state->SupportJobExperience.Length <= Id.Index())
            {
                return 0;
            }

            return state->SupportJobExperience[Id.Index()];
        }
    }

    public uint TotalExperience
    {
        get
        {
            IEnumerable<MKDGrowDataSJob> rows = GrowthData.Where(r => r.SubrowId < Level);

            return (uint)rows.Sum(r => r.Unknown0) + CurrentExperience;
        }
    }

    public uint StatusId => PhantomJobStatuses.For(Id);
}
