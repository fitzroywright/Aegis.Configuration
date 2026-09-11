using Aegis.Configuration.Domain;

namespace Aegis.Configuration.Application;

public sealed record ContractValidationResult(bool IsValid, IReadOnlyList<string> Errors);

public sealed class ContractValidator
{
    public ContractValidationResult Validate(ApplicationConfigurationContract contract)
    {
        List<string> errors = [];
        if (string.IsNullOrWhiteSpace(contract.ApplicationId)) errors.Add("ApplicationId is required.");
        if (string.IsNullOrWhiteSpace(contract.Version)) errors.Add("Version is required.");
        HashSet<string> ids = new(StringComparer.OrdinalIgnoreCase);
        foreach (ConfigurationRequirement requirement in contract.Requirements)
        {
            if (string.IsNullOrWhiteSpace(requirement.Id)) errors.Add("Every requirement must have an Id.");
            else if (!ids.Add(requirement.Id)) errors.Add($"Duplicate requirement Id '{requirement.Id}'.");
            if (requirement.Kind == ConfigurationRequirementKind.Secret && string.IsNullOrWhiteSpace(requirement.SecretName)) errors.Add($"Secret requirement '{requirement.Id}' must declare SecretName.");
            if (requirement.Kind != ConfigurationRequirementKind.Secret && requirement.Required && string.IsNullOrWhiteSpace(requirement.ConfigurationKey)) errors.Add($"Required requirement '{requirement.Id}' must declare ConfigurationKey.");
            if (requirement.Kind == ConfigurationRequirementKind.Secret && requirement.FingerprintSafe) errors.Add($"Secret requirement '{requirement.Id}' cannot enable FingerprintSafe.");
        }
        return new(errors.Count == 0, errors);
    }
}
