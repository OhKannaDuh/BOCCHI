using BOCCHI.Common.Data.SupportJobs;

namespace BOCCHI.Automator.Data.StateMemory;

public class SupportJobMemory(SupportJobId job)
{
    public readonly SupportJobId Job = job;
}
