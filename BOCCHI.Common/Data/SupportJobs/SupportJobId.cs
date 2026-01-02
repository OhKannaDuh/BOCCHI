namespace BOCCHI.Common.Data.SupportJobs;

public enum SupportJobId
{
    PhantomFreelancer = 0,
    PhantomKnight = 1,
    PhantomBerserker = 2,
    PhantomMonk = 3,
    PhantomRanger = 4,
    PhantomSamurai = 5,
    PhantomBard = 6,
    PhantomGeomancer = 7,
    PhantomTime = 8,
    PhantomCannoneer = 9,
    PhantomChemist = 10,
    PhantomOracle = 11,
    PhantomThief = 12,
    PhantomMysticKnight = 13,
    PhantomGladiator = 14,
    PhantomDancer = 15,
}

public static class SupportJobExtensions
{
    extension(SupportJobId jobId)
    {
        public int Index()
        {
            return (int)jobId;
        }

        public uint RowId()
        {
            return (uint)jobId;
        }
    }
}
