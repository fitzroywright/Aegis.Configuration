namespace Aegis.Configuration.Domain;

public enum ConfigurationRequirementKind { Configuration, Secret, ExternalService, Diagnostic }
public enum ConfigurationState { NotRequired, Missing, Invalid, ConfiguredUnverified, Verified }

public sealed record ApplicationConfigurationContract(string ApplicationId, string DisplayName, string Version, IReadOnlyList<ConfigurationRequirement> Requirements);

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
    bool FingerprintSafe = false);

public sealed record ConfigurationEvaluation(string ApplicationId, string Version, IReadOnlyList<RequirementEvaluation> Requirements)
{
    public bool IsOperational => Requirements.All(x => !x.Required || x.State is ConfigurationState.Verified or ConfigurationState.NotRequired);
}

public sealed record RequirementEvaluation(string RequirementId, string DisplayName, bool Required, ConfigurationState State, string Message);
