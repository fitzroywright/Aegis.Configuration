namespace Aegis.Configuration.Domain;

public enum ConfigurationRequirementKind { Configuration, Secret, ExternalService }
public enum ConfigurationState { OptionalNotConfigured, Missing, Configured }

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
    string? InstructionId = null,
    bool? IsConfigured = null,
    string? SafeDisplayValue = null,
    string? Example = null,
    bool Sensitive = false);

public sealed record ConfigurationEvaluation(string ApplicationId, string Version, IReadOnlyList<RequirementEvaluation> Requirements)
{
    public bool IsComplete => Requirements
        .Where(x => x.Required)
        .All(x => x.State == ConfigurationState.Configured);
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
