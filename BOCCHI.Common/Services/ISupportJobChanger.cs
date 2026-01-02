using BOCCHI.Common.Data.SupportJobs;

namespace BOCCHI.Common.Services;

public interface ISupportJobChanger
{
    void Change(SupportJobId id);

    bool IsBusy();
}
