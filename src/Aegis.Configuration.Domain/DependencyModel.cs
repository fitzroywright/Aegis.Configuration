namespace Aegis.Configuration.Domain;

public enum DependencyKind { Application, CommonService, Database, SecretProvider, Messaging, ExternalService }
public sealed record SystemDependency(string Id, string DisplayName, DependencyKind Kind, bool Critical = true);
public sealed record ApplicationDependencyMap(string ApplicationId, IReadOnlyList<SystemDependency> Dependencies);
public sealed record DependencyHealth(string DependencyId, bool Available, string Message);
public sealed record BlastRadiusImpact(string ApplicationId, string DependencyId, bool Critical, string Reason);
public sealed record BlastRadiusReport(string FailedDependencyId, IReadOnlyList<BlastRadiusImpact> Impacts);
