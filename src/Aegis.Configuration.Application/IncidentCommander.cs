using Aegis.Configuration.Domain;

namespace Aegis.Configuration.Application;

public sealed record IncidentSummary(
    Guid IncidentId,
    DateTimeOffset CreatedAtUtc,
    OperationalRiskLevel Severity,
    string Headline,
    IReadOnlyList<string> RootSuspects,
    IReadOnlyList<string> AffectedApplications,
    IReadOnlyList<RemediationStep> RecoveryPlan,
    IReadOnlyList<string> Evidence);

public sealed class IncidentCommander
{
    public IncidentSummary Build(OperationalRiskReport risk, RemediationPlan plan)
    {
        OperationalRiskItem[] affected = risk.Items.Where(x => x.Level != OperationalRiskLevel.Healthy).ToArray();
        OperationalRiskLevel severity = affected.Length == 0 ? OperationalRiskLevel.Healthy : affected.Max(x => x.Level);
        string[] suspects = affected
            .Select(x => ExtractQuoted(x.Cause))
            .Where(x => x is not null)
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        string headline = severity == OperationalRiskLevel.Healthy
            ? "Fleet healthy"
            : $"{affected.Length} application(s) require attention; highest severity {severity}.";
        return new(Guid.NewGuid(), DateTimeOffset.UtcNow, severity, headline, suspects,
            affected.Select(x => x.ApplicationId).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            plan.Steps,
            affected.SelectMany(x => x.Evidence).Distinct().ToArray());
    }

    private static string? ExtractQuoted(string value)
    {
        int start = value.IndexOf('\'');
        if (start < 0) return null;
        int end = value.IndexOf('\'', start + 1);
        return end > start ? value[(start + 1)..end] : null;
    }
}
