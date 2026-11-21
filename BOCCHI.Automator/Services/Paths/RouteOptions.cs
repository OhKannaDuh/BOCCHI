using BOCCHI.Common.Data.Zones.Graph.Traversal;

namespace BOCCHI.Automator.Services.Paths;

public static class RouteOptions
{
    public static List<IGraphCandidateCalculator> Calculators(bool considerReturn, bool preferReturn)
    {
        List<IGraphCandidateCalculator> calculators = [];
        if (!considerReturn || !preferReturn)
        {
            calculators.Add(new WalkTeleportWalkCalculator());
        }

        calculators.Add(new DirectWalkCalculator());
        if (considerReturn)
        {
            calculators.Add(new ReturnTeleportWalkCalculator());
        }

        return calculators;
    }
}
