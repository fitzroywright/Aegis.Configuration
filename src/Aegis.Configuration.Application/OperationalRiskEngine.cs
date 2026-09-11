using Aegis.Configuration.Domain;

namespace Aegis.Configuration.Application;

public enum OperationalRiskLevel
{
    Healthy,
    Advisory,
    Degraded,
    Critical
}

public sealed record OperationalRiskItem(
    string ApplicationId,
    OperationalRiskLevel Level,
    int Priority,
    string Cause,
    string RecommendedAction,
    IReadOnlyList<string> Evidence);

public sealed record OperationalRiskReport(
    DateTimeOffset EvaluatedAtUtc,
    IReadOnlyList<OperationalRiskItem> Items)
{
    public bool HasCriticalRisk => Items.Any(x => x.Level == OperationalRiskLevel.Critical);
}

public sealed class OperationalRiskEngine
{
    public OperationalRiskReport Analyze(
        IReadOnlyList<CommissioningSnapshot> snapshots,
        IReadOnlyDictionary<string, ConfigurationDriftReport> driftReports,
        IReadOnlyList<BlastRadiusReport> dependencyFailures)
    {
        List<OperationalRiskItem> items = [];
        foreach (CommissioningSnapshot snapshot in snapshots)
        {
            List<string> evidence = [];
            OperationalRiskLevel level = snapshot.Evaluation.IsOperational ? OperationalRiskLevel.Healthy : OperationalRiskLevel.Degraded;
            int priority = snapshot.Evaluation.IsOperational ? 0 : 50;
            string cause = snapshot.Evaluation.IsOperational ? "All required configuration is verified." : "One or more required configuration items are not verified.";
            string action = snapshot.Evaluation.IsOperational ? "No action required." : "Open commissioning details and remediate missing or invalid requirements.";

            RequirementEvaluation[] bad = snapshot.Evaluation.Requirements.Where(x => x.Required && x.State is ConfigurationState.Missing or ConfigurationState.Invalid).ToArray();
            evidence.AddRange(bad.Select(x => $"{x.DisplayName}: {x.State}"));

            if (driftReports.TryGetValue(snapshot.ApplicationId, out ConfigurationDriftReport? drift) && drift.HasCriticalDrift)
            {
                level = OperationalRiskLevel.Critical;
                priority += 35;
                cause = "Previously commissioned configuration has critically drifted.";
                action = "Restore the last known-good configuration or recommission the changed requirement.";
                evidence.AddRange(drift.Changes.Where(x => x.Severity == DriftSeverity.Critical).Select(x => $"Drift {x.RequirementId}: {x.Message}"));
            }

            foreach (BlastRadiusReport failure in dependencyFailures)
            {
                DependencyImpact? impact = failure.Impacts.FirstOrDefault(x => string.Equals(x.ApplicationId, snapshot.ApplicationId, StringComparison.OrdinalIgnoreCase));
                if (impact is null) continue;
                level = impact.Critical ? OperationalRiskLevel.Critical : Max(level, OperationalRiskLevel.Advisory);
                priority += impact.Critical ? 40 : 15;
                cause = impact.Critical ? $"Critical dependency '{failure.FailedDependencyId}' is unavailable." : $"Optional dependency '{failure.FailedDependencyId}' is unavailable.";
                action = $"Restore {failure.FailedDependencyId}; dependency path: {string.Join(" -> ", impact.Path)}.";
                evidence.Add($"Dependency: {string.Join(" -> ", impact.Path)}");
            }

            items.Add(new(snapshot.ApplicationId, level, Math.Min(priority, 100), cause, action, evidence));
        }
        return new(DateTimeOffset.UtcNow, items.OrderByDescending(x => x.Priority).ThenBy(x => x.ApplicationId).ToArray());
    }

    private static OperationalRiskLevel Max(OperationalRiskLevel left, OperationalRiskLevel right) => left > right ? left : right;
}
