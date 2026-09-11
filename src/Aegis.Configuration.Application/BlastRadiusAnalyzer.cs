using Aegis.Configuration.Domain;

namespace Aegis.Configuration.Application;

public sealed class BlastRadiusAnalyzer
{
    public BlastRadiusReport Analyze(string failedDependencyId, IEnumerable<ApplicationDependencyMap> maps)
    {
        List<BlastRadiusImpact> impacts = [];
        foreach (ApplicationDependencyMap map in maps)
        {
            foreach (SystemDependency dependency in map.Dependencies.Where(x => string.Equals(x.Id, failedDependencyId, StringComparison.OrdinalIgnoreCase)))
            {
                impacts.Add(new(map.ApplicationId, dependency.Id, dependency.Critical, dependency.Critical ? "Application operation is expected to be impaired." : "Optional capability may be degraded."));
            }
        }
        return new(failedDependencyId, impacts.OrderByDescending(x => x.Critical).ThenBy(x => x.ApplicationId).ToArray());
    }
}
