namespace Aegis.Configuration.Domain;

public sealed record ApplicationDependencyContract(
    string ApplicationId,
    string Version,
    IReadOnlyList<ApplicationDependency> Dependencies,
    string? SiteId = null,
    string? InstanceId = null,
    DateTimeOffset? LastRegisteredAtUtc = null);

public sealed record ApplicationDependency(
    string Id,
    string DisplayName,
    bool Required,
    string Criticality,
    string Purpose,
    string DegradedBehavior,
    string Interface,
    string? Engine = null,
    string? Endpoint = null,
    bool? Reachable = null,
    string? Evidence = null);

public sealed record ApplicationEnvironmentContract(
    string ApplicationId,
    string Version,
    DetectedEnvironment Environment,
    string? SiteId = null,
    string? InstanceId = null,
    DateTimeOffset? LastRegisteredAtUtc = null);

public sealed record DetectedEnvironment(
    string? OperatingSystem = null,
    string? OperatingSystemVersion = null,
    string? CpuArchitecture = null,
    int? ProcessorCount = null,
    long? AvailableMemoryBytes = null,
    long? ProcessWorkingSetBytes = null,
    string? RuntimeVersion = null,
    bool? NetworkAvailable = null,
    IReadOnlyList<DetectedDisk>? Disks = null,
    IReadOnlyList<DetectedPath>? Paths = null,
    IReadOnlyList<DetectedEndpoint>? Endpoints = null,
    IReadOnlyList<DetectedHardware>? Hardware = null,
    DateTimeOffset? DetectedAtUtc = null);

public sealed record DetectedDisk(string Path, long? FreeBytes, long? TotalBytes);
public sealed record DetectedPath(string Path, bool Exists, bool? Writable);
public sealed record DetectedEndpoint(string Name, string Endpoint, bool? Reachable, string? Detail = null);
public sealed record DetectedHardware(string Kind, string Name, string? Identifier = null, string? Detail = null);

public sealed record ApplicationOperationalRegistration(
    ApplicationConfigurationContract Configuration,
    ApplicationDependencyContract Dependencies,
    ApplicationEnvironmentContract Environment);
