using Aegis.Configuration.Domain;

namespace Aegis.Configuration.Application;

public sealed class FleetIncidentService(
    BlastRadiusAnalyzer blastRadius,
    OperationalRiskEngine riskEngine,
    RemediationPlanner remediationPlanner,
    IncidentCommander incidentCommander)
{
    public IncidentSummary Analyze(FleetStatusSnapshot fleet, DependencyGraph graph, IReadOnlyCollection<string> failedDependencies)
    {
        BlastRadiusReport[] failures = failedDependencies
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(x => blastRadius.Analyze(graph, x))
            .ToArray();
        OperationalRiskReport risk = riskEngine.Analyze(fleet.Applications, new Dictionary<string, ConfigurationDriftReport>(), failures);
        return incidentCommander.Build(risk, remediationPlanner.Build(risk));
    }
}
