using Aegis.Configuration.Application;

namespace Aegis.Configuration.Tests;

public sealed class IncidentCommanderTests
{
    [Fact]
    public void Build_CreatesIncidentFromCriticalFleetRisk()
    {
        OperationalRiskReport risk = new(DateTimeOffset.UtcNow, [new("Studio", OperationalRiskLevel.Critical, 90, "Critical dependency 'OpenBao' is unavailable.", "Restore OpenBao.", ["OpenBao -> Common.Secrets -> Studio"])]);
        RemediationPlan plan = new RemediationPlanner().Build(risk);
        IncidentSummary incident = new IncidentCommander().Build(risk, plan);
        Assert.Equal(OperationalRiskLevel.Critical, incident.Severity);
        Assert.Contains("OpenBao", incident.RootSuspects);
        Assert.Contains("Studio", incident.AffectedApplications);
    }
}
