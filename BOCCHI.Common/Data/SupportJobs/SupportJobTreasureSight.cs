using BOCCHI.Common.Data.OccultCrescent;
using BOCCHI.Common.Services;

namespace BOCCHI.Common.Data.SupportJobs;

public static class SupportJobTreasureSight
{
    public static byte RequiredFreelancerLevel => PhantomActions.TreasuresightUnlockLevel;

    public static bool CanCast(ISupportJobFactory supportJobs) =>
        supportJobs.Create(SupportJobId.PhantomFreelancer).Level >= RequiredFreelancerLevel;
}
