namespace Aegis.Configuration.Domain;

public enum ConfigurationRequirementKind { Configuration, Secret, ExternalService, Diagnostic }
public enum ConfigurationState { NotRequired, Missing, Invalid, ConfiguredUnverified, Verified }

public sealed record ApplicationConfigurationContract(
    string ApplicationId,
    string DisplayName,
    string Version,
    IReadOnlyList<ConfigurationRequirement> Requirements,
    string? SiteId = null,
    string? InstanceId = null,
    string? Description = null);

public sealed record ConfigurationRequirement(
    string Id,
    string DisplayName,
    ConfigurationRequirementKind Kind,
    bool Required,
    string Purpose,
    string? ConfigurationKey = null,
    string? SecretName = null,
    string? Validator = null,
    string? InstructionId = null,
    bool FingerprintSafe = false,
    bool? IsConfigured = null,
    string? SafeDisplayValue = null,
    string? Example = null,
    bool Sensitive = false);

public sealed record ConfigurationEvaluation(string ApplicationId, string Version, IReadOnlyList<RequirementEvaluation> Requirements)
{
    // Configuration answers only whether required values have been supplied.
    // Reachability, authentication, health and operational validation belong to Aegis.Diagnostics.
    public bool IsOperational => Requirements
        .Where(x => x.Required)
        .All(x => x.State is ConfigurationState.ConfiguredUnverified or ConfigurationState.Verified);
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
    bool Sensitive = false);
