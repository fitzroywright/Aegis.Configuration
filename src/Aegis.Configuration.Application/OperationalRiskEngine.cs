using Aegis.Configuration.Domain;

namespace Aegis.Configuration.Application;

public enum ConfigurationRiskLevel { Healthy, Advisory, Degraded, Critical }

public sealed record ConfigurationRiskItem(string ApplicationId, ConfigurationRiskLevel Level, int Priority, string Cause, string RecommendedAction, IReadOnlyList<string> Evidence);
public sealed record ConfigurationRiskReport(DateTimeOffset EvaluatedAtUtc, IReadOnlyList<ConfigurationRiskItem> Items);

public sealed class OperationalRiskEngine
{
    public ConfigurationRiskReport Analyze(IReadOnlyList<CommissioningSnapshot> snapshots, IReadOnlyDictionary<string, ConfigurationDriftReport> driftReports)
    {
        List<ConfigurationRiskItem> items = [];
        foreach (CommissioningSnapshot snapshot in snapshots)
        {
            RequirementEvaluation[] bad = snapshot.Evaluation.Requirements.Where(x => x.Required && x.State is ConfigurationState.Missing or ConfigurationState.Invalid).ToArray();
            ConfigurationRiskLevel level = bad.Length == 0 ? ConfigurationRiskLevel.Healthy : ConfigurationRiskLevel.Degraded;
            int priority = bad.Length == 0 ? 0 : 50;
            string cause = bad.Length == 0 ? "Required configuration is verified." : "Required configuration is missing or invalid.";
            string action = bad.Length == 0 ? "No configuration action required." : "Open commissioning instructions and correct the affected configuration requirements.";
            List<string> evidence = bad.Select(x => $"{x.DisplayName}: {x.State}").ToList();
            if (driftReports.TryGetValue(snapshot.ApplicationId, out ConfigurationDriftReport? drift) && drift.HasCriticalDrift)
            {
                level = ConfigurationRiskLevel.Critical;
                priority = Math.Min(100, priority + 35);
                cause = "Commissioned configuration has critically drifted from its recorded baseline.";
                action = "Review the configuration drift and restore or recommission the changed requirement.";
                evidence.AddRange(drift.Changes.Where(x => x.Severity == DriftSeverity.Critical).Select(x => $"Drift {x.RequirementId}: {x.Message}"));
            }
            items.Add(new(snapshot.ApplicationId, level, priority, cause, action, evidence));
        }
        return new(DateTimeOffset.UtcNow, items.OrderByDescending(x => x.Priority).ThenBy(x => x.ApplicationId).ToArray());
    }
}
