using Aegis.Configuration.Domain;

namespace Aegis.Configuration.Application;

public sealed record RemediationStep(int Order, string Target, string Action, string Reason, bool Critical);
public sealed record RemediationPlan(DateTimeOffset CreatedAtUtc, IReadOnlyList<RemediationStep> Steps);

public sealed class RemediationPlanner
{
    public RemediationPlan Build(OperationalRiskReport riskReport)
    {
        List<RemediationStep> steps = [];
        int order = 1;
        foreach (OperationalRiskItem item in riskReport.Items.Where(x => x.Level != OperationalRiskLevel.Healthy).OrderByDescending(x => x.Priority))
        {
            steps.Add(new(order++, item.ApplicationId, item.RecommendedAction, item.Cause, item.Level == OperationalRiskLevel.Critical));
        }
        return new(DateTimeOffset.UtcNow, steps);
    }
}
