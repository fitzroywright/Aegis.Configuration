namespace Aegis.Configuration.Application;

public sealed record ConfigurationRemediationStep(int Order, string ApplicationId, string Action, string Reason, bool Critical);
public sealed record ConfigurationRemediationPlan(DateTimeOffset CreatedAtUtc, IReadOnlyList<ConfigurationRemediationStep> Steps);

public sealed class RemediationPlanner
{
    public ConfigurationRemediationPlan Build(ConfigurationRiskReport riskReport)
    {
        int order = 1;
        ConfigurationRemediationStep[] steps = riskReport.Items
            .Where(x => x.Level != ConfigurationRiskLevel.Healthy)
            .OrderByDescending(x => x.Priority)
            .Select(x => new ConfigurationRemediationStep(order++, x.ApplicationId, x.RecommendedAction, x.Cause, x.Level == ConfigurationRiskLevel.Critical))
            .ToArray();
        return new(DateTimeOffset.UtcNow, steps);
    }
}
