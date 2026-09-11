using Aegis.Configuration.Domain;

namespace Aegis.Configuration.Application;

public enum DriftSeverity { None, Improved, Warning, Critical }
public sealed record RequirementDrift(string RequirementId, ConfigurationState? Previous, ConfigurationState Current, DriftSeverity Severity, string Message);
public sealed record ConfigurationDriftReport(string ApplicationId, DateTimeOffset ComparedAtUtc, IReadOnlyList<RequirementDrift> Changes)
{
    public bool HasCriticalDrift => Changes.Any(x => x.Severity == DriftSeverity.Critical);
}

public sealed class DriftDetector
{
    public ConfigurationDriftReport Compare(CommissioningHistoryEntry previous, CommissioningSnapshot current)
    {
        Dictionary<string, RequirementEvaluation> old = previous.Requirements.ToDictionary(x => x.RequirementId, StringComparer.OrdinalIgnoreCase);
        List<RequirementDrift> changes = [];
        foreach (RequirementEvaluation now in current.Evaluation.Requirements)
        {
            old.TryGetValue(now.RequirementId, out RequirementEvaluation? before);
            if (before?.State == now.State) continue;
            DriftSeverity severity = Classify(before?.State, now.State);
            changes.Add(new(now.RequirementId, before?.State, now.State, severity, $"{before?.State.ToString() ?? "New"} -> {now.State}"));
        }
        return new(current.ApplicationId, DateTimeOffset.UtcNow, changes);
    }

    private static DriftSeverity Classify(ConfigurationState? previous, ConfigurationState current)
    {
        if (current is ConfigurationState.Missing or ConfigurationState.Invalid) return DriftSeverity.Critical;
        if (current == ConfigurationState.ConfiguredUnverified && previous == ConfigurationState.Verified) return DriftSeverity.Warning;
        if (current == ConfigurationState.Verified) return DriftSeverity.Improved;
        return DriftSeverity.Warning;
    }
}
