using Aegis.Configuration.Domain;

namespace Aegis.Configuration.Application;

public sealed record FailureCorrelation(string DependencyId, int AffectedApplications, int CriticalApplications, double Confidence, IReadOnlyList<string> Applications);

public sealed class FailureCorrelationEngine
{
    public IReadOnlyList<FailureCorrelation> Rank(IReadOnlyList<BlastRadiusReport> candidates, IReadOnlyList<CommissioningSnapshot> snapshots)
    {
        HashSet<string> unhealthy = snapshots.Where(x => !x.Evaluation.IsOperational).Select(x => x.ApplicationId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        List<FailureCorrelation> results = [];
        foreach (BlastRadiusReport candidate in candidates)
        {
            string[] affected = candidate.Impacts.Select(x => x.ApplicationId).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (affected.Length == 0) continue;
            int matches = affected.Count(unhealthy.Contains);
            int critical = candidate.Impacts.Count(x => x.Critical && unhealthy.Contains(x.ApplicationId));
            double confidence = Math.Round((double)matches / affected.Length * 100, 1);
            results.Add(new(candidate.FailedDependencyId, matches, critical, confidence, affected.Where(unhealthy.Contains).ToArray()));
        }
        return results.OrderByDescending(x => x.Confidence).ThenByDescending(x => x.CriticalApplications).ThenByDescending(x => x.AffectedApplications).ToArray();
    }
}
