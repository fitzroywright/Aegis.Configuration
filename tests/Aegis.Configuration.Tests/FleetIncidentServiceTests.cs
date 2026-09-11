using Aegis.Configuration.Application;
using Aegis.Configuration.Domain;

namespace Aegis.Configuration.Tests;

public sealed class FleetIncidentServiceTests
{
    [Fact]
    public void Analyze_ConvertsSharedDependencyFailureIntoIncident()
    {
        FleetStatusSnapshot fleet = new(DateTimeOffset.UtcNow, [new("app", "1", DateTimeOffset.UtcNow, new("app", "1", [new("secret", "Secret", true, ConfigurationState.Verified, "OK")]))]);
        DependencyGraph graph = new([new("OpenBao", "OpenBao", DependencyKind.SecretProvider), new("app", "App", DependencyKind.Application)], [new("app", "OpenBao", true, "Secrets")]);
        FleetIncidentService service = new(new(), new(), new(), new());
        IncidentSummary incident = service.Analyze(fleet, graph, ["OpenBao"]);
        Assert.Equal(OperationalRiskLevel.Critical, incident.Severity);
        Assert.Contains("app", incident.AffectedApplications);
    }
}
