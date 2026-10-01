using BOCCHI.Common.Config;
using BOCCHI.Common.Data.OccultCrescent;
using BOCCHI.Common.Services;

namespace BOCCHI.Common.Data.SupportJobs;

public static class TriageRaiseJob
{
    public static bool AnyUnlocked(ISupportJobFactory supportJobs) =>
        IsUnlocked(supportJobs, SupportJobId.PhantomChemist)
        || IsUnlocked(supportJobs, SupportJobId.PhantomWhiteMage);

    public static bool TrySelect(
        ISupportJobFactory supportJobs,
        TriageRaiseJobPreference preference,
        out SupportJobId jobId)
    {
        SupportJobId preferred = preference == TriageRaiseJobPreference.PhantomWhiteMage
            ? SupportJobId.PhantomWhiteMage
            : SupportJobId.PhantomChemist;
        SupportJobId fallback = preferred == SupportJobId.PhantomChemist
            ? SupportJobId.PhantomWhiteMage
            : SupportJobId.PhantomChemist;

        if (IsUnlocked(supportJobs, preferred))
        {
            jobId = preferred;
            return true;
        }

        if (IsUnlocked(supportJobs, fallback))
        {
            jobId = fallback;
            return true;
        }

        jobId = default;
        return false;
    }

    public static uint RaiseActionId(SupportJobId jobId) =>
        jobId == SupportJobId.PhantomWhiteMage ? PhantomActions.OccultRaise : PhantomActions.Revive;

    private static bool IsUnlocked(ISupportJobFactory supportJobs, SupportJobId jobId) =>
        supportJobs.Create(jobId).Level >= 1;
}
