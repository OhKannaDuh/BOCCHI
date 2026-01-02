namespace BOCCHI.Common.Data.SupportJobs;

public interface ISupportJobFactory
{
    SupportJob Create(SupportJobId id);

    bool TryGetCurrent(out SupportJob current);

    IEnumerable<SupportJob> All();
}
