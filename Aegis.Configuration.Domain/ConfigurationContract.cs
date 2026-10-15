namespace Aegis.Configuration.Domain;

public enum ConfigurationRequirementKind { Configuration, Secret, ExternalService }
public enum ConfigurationState { OptionalNotConfigured, Disabled, Missing, Configured, Default, Unknown }

public sealed record ApplicationConfigurationContract(
    string ApplicationId,
    string DisplayName,
    string Version,
    IReadOnlyList<ConfigurationRequirement> Requirements,
    string? SiteId = null,
    string? InstanceId = null,
    string? Description = null,
    DateTimeOffset? LastRegisteredAtUtc = null,
    RuntimeEnvironmentSnapshot? RuntimeEnvironment = null,
    DiagnosticsCapability? Diagnostics = null,
    SecretProviderDeclaration? Secrets = null,
    string? RunbookReference = null,
    IReadOnlyList<ExternalDependencyDeclaration>? Dependencies = null,
    ApplicationPresentationMetadata? Presentation = null);

/// <summary>
/// Describes how an application says it obtains secrets. Aegis.Configuration may observe,
/// diagnose and assist with this provider, but does not own the secret values or change the
/// application's selected provider.
/// </summary>

public sealed record ApplicationPresentationMetadata(
    string? IconUrl = null,
    string? ShortName = null,
    string? Accent = null);

public sealed record ExternalDependencyDeclaration(
    string Name,
    string Kind,
    bool Required,
    string? EndpointConfigurationKey = null,
    string? Description = null,
    string? RunbookReference = null);

public sealed record ConfigurationReadiness(
    string ApplicationId,
    int RequiredPresent,
    int RequiredTotal,
    int OptionalPresent,
    int OptionalTotal,
    IReadOnlyList<string> MissingRequiredNames,
    DateTimeOffset ObservedAtUtc)
{
    public bool IsReady => RequiredPresent == RequiredTotal;
    public string RequiredSummary => $"{RequiredPresent}/{RequiredTotal} required settings present";
    public string OptionalSummary => $"{OptionalPresent}/{OptionalTotal} optional settings present";
}

public sealed record SecretProviderDeclaration(
    string? Provider = null,
    string? ManagementUrl = null,
    string? ConnectionStatus = null,
    string? CredentialStatus = null);

/// <summary>Deployment identity only. Live health and capacity telemetry belongs to Aegis.Diagnostics.</summary>
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
    string? Format = null);

/// <summary>Describes diagnostic capability only; it never carries health state or credential values.</summary>
public sealed record DiagnosticsCapability(
    string? BaseUrl = null,
    string HealthPath = "/health",
    string DiagnosticsRunPath = "/api/engineering/diagnostics/run",
    string DiagnosticsRunsPath = "/api/engineering/diagnostics/runs",
    string TelemetryPath = "/api/engineering/diagnostics/telemetry",
    bool SupportsRemoteDiagnostics = true,
    bool SupportsOperationalTelemetry = true,
    string AuthenticationScheme = "MachineCredential",
    string SecretName = "",
    IReadOnlyList<int>? SupportedLevels = null);

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
    string? DisplayValue = null,
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
    public string? RegistrationState { get; init; }
    public string? LifecycleHealth { get; init; }
    public string? InventoryError { get; init; }
    public bool EvaluationSuspended { get; init; }
    public bool NeedsAttention => InventoryError is not null || (!EvaluationSuspended && !IsComplete) ||
        (LifecycleHealth?.Split(',').Any(x => x.Contains(": Failed", StringComparison.OrdinalIgnoreCase) || x.Contains(": Stalled", StringComparison.OrdinalIgnoreCase) || x.Contains(": Slow", StringComparison.OrdinalIgnoreCase)) ?? false);
    public bool IsComplete => !EvaluationSuspended && Requirements.Where(x => x.Required).All(x => x.State is ConfigurationState.Configured or ConfigurationState.Default);

    public ConfigurationReadiness ToReadiness(DateTimeOffset? observedAtUtc = null)
    {
        RequirementEvaluation[] required = Requirements.Where(x => x.Required).ToArray();
        RequirementEvaluation[] optional = Requirements.Where(x => !x.Required).ToArray();
        static bool Present(RequirementEvaluation x) =>
            x.State is ConfigurationState.Configured or ConfigurationState.Default ||
            (!x.Required && x.State == ConfigurationState.Disabled);

        return new(
            ApplicationId,
            required.Count(Present),
            required.Length,
            optional.Count(Present),
            optional.Length,
            required.Where(x => !Present(x)).Select(x => x.DisplayName).ToArray(),
            observedAtUtc ?? DateTimeOffset.UtcNow);
    }
}

public sealed record RequirementEvaluation(
    string RequirementId,
    string DisplayName,
    bool Required,
    ConfigurationState State,
    string Message,
    string? Purpose = null,
    string? DisplayValue = null,
    string? Example = null,
    bool Sensitive = false,
    bool ExplicitlyConfigured = false,
    bool EffectiveValueAvailable = false,
    string? EffectiveSource = null,
    bool HasDefault = false,
    string? DefaultValue = null,
    string? ConditionalOn = null,
    string? VerificationStatus = null);

