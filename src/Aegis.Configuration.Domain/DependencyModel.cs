namespace Aegis.Configuration.Domain;

public enum DependencyKind
{
    Application,
    CommonService,
    Database,
    SecretProvider,
    Messaging,
    ExternalService
}

public sealed record DependencyNode(string Id, string DisplayName, DependencyKind Kind);

public sealed record DependencyEdge(
    string ConsumerId,
    string ProviderId,
    bool Critical,
    string Purpose,
    string? RemediationInstructionId = null);

public sealed record DependencyGraph(
    IReadOnlyList<DependencyNode> Nodes,
    IReadOnlyList<DependencyEdge> Edges);

public sealed record DependencyImpact(
    string ApplicationId,
    string FailedDependencyId,
    bool Critical,
    int Distance,
    IReadOnlyList<string> Path,
    string Purpose);

public sealed record BlastRadiusReport(
    string FailedDependencyId,
    DateTimeOffset EvaluatedAtUtc,
    IReadOnlyList<DependencyImpact> Impacts);
