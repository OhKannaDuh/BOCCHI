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
            var state = PublicContentOccultCrescent.GetState();
            if (state == null || state->SupportJobLevels.Length < Id.Index())
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
            var state = PublicContentOccultCrescent.GetState();
            if (state == null || state->SupportJobExperience.Length < Id.Index())
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
            var rows = GrowthData.Where(r => r.SubrowId < Level);

            return (uint)rows.Sum(r => r.Unknown0) + CurrentExperience;
        }
    }

    public uint StatusId
    {
        get
        {
            return Id switch
            {
                SupportJobId.PhantomFreelancer => 4263,
                SupportJobId.PhantomKnight => 4242,
                SupportJobId.PhantomBerserker => 4358,
                SupportJobId.PhantomMonk => 4359,
                SupportJobId.PhantomRanger => 4360,
                SupportJobId.PhantomSamurai => 4361,
                SupportJobId.PhantomBard => 4362,
                SupportJobId.PhantomGeomancer => 4363,
                SupportJobId.PhantomTime => 4364,
                SupportJobId.PhantomCannoneer => 4365,
                SupportJobId.PhantomChemist => 4366,
                SupportJobId.PhantomOracle => 4367,
                SupportJobId.PhantomThief => 4368,
                SupportJobId.PhantomMysticKnight => 4803,
                SupportJobId.PhantomGladiator => 4804,
                SupportJobId.PhantomDancer => 4805,
                _ => throw new ArgumentOutOfRangeException(),
            };
        }
    }
}
