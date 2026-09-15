namespace Aegis.Configuration.Domain;

public enum ConfigurationRequirementKind { Configuration, Secret, ExternalService }
public enum ConfigurationState { OptionalNotConfigured, Missing, Configured, Default, Unknown }

public sealed record ApplicationConfigurationContract(
    string ApplicationId,
    string DisplayName,
    string Version,
    IReadOnlyList<ConfigurationRequirement> Requirements,
    string? SiteId = null,
    string? InstanceId = null,
    string? Description = null,
    DateTimeOffset? LastRegisteredAtUtc = null,
    RuntimeEnvironmentSnapshot? RuntimeEnvironment = null);

public sealed record RuntimeEnvironmentSnapshot(
    string? MachineName = null,
    string? HostName = null,
    string? DnsDomainName = null,
    string? HostingEnvironment = null,
    string? OperatingSystem = null,
    string? OsArchitecture = null,
    string? ProcessArchitecture = null,
    string? FrameworkDescription = null,
    int? ProcessorCount = null,
    long? RuntimeAvailableMemoryBytes = null,
    string? ContentRootPath = null,
    IReadOnlyList<RuntimeNetworkInterface>? NetworkInterfaces = null,
    IReadOnlyList<RuntimeStorageVolume>? Storage = null);

public sealed record RuntimeNetworkInterface(
    string? Name = null,
    string? Type = null,
    IReadOnlyList<string>? Addresses = null,
    IReadOnlyList<string>? Gateways = null);

public sealed record RuntimeStorageVolume(
    string? Name = null,
    string? DriveType = null,
    string? Format = null,
    long? TotalBytes = null,
    long? AvailableBytes = null);

public sealed record ConfigurationRequirement(
    string Id,
    string DisplayName,
    ConfigurationRequirementKind Kind,
    bool Required,
    string Purpose,
    string? ConfigurationKey = null,
    string? SecretName = null,
    string? InstructionId = null,
    bool? IsConfigured = null,
    string? SafeDisplayValue = null,
    string? Example = null,
    bool Sensitive = false,
    string? ConfigurationState = null,
    bool? EffectiveValueAvailable = null,
    string? EffectiveSource = null,
    bool HasDefault = false,
    string? DefaultValue = null,
    string? ConditionalOn = null,
    string? VerificationStatus = null,
    string? SecretPath = null);

public sealed record ConfigurationEvaluation(string ApplicationId, string Version, IReadOnlyList<RequirementEvaluation> Requirements)
{
    public bool IsComplete => Requirements.Where(x => x.Required).All(x => x.State is ConfigurationState.Configured or ConfigurationState.Default);
}

public sealed record RequirementEvaluation(
    string RequirementId,
    string DisplayName,
    bool Required,
    ConfigurationState State,
    string Message,
    string? Purpose = null,
    string? SafeDisplayValue = null,
    string? Example = null,
    bool Sensitive = false,
    bool ExplicitlyConfigured = false,
    bool EffectiveValueAvailable = false,
    string? EffectiveSource = null,
    bool HasDefault = false,
    string? DefaultValue = null,
    string? ConditionalOn = null,
    string? VerificationStatus = null);
