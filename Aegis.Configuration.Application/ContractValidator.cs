using Aegis.Configuration.Domain;

namespace Aegis.Configuration.Application;

public sealed record ContractValidationResult(bool IsValid, IReadOnlyList<string> Errors);

public sealed class ContractValidator
{
    private static readonly HashSet<string> KnownValidators = new(StringComparer.OrdinalIgnoreCase)
    {
        "secret",
        "uri",
        "http",
        "tcp",
        "postgresql"
    };

    public ContractValidationResult Validate(ApplicationConfigurationContract contract)
    {
        List<string> errors = [];
        if (string.IsNullOrWhiteSpace(contract.ApplicationId)) errors.Add("ApplicationId is required.");
        if (string.IsNullOrWhiteSpace(contract.DisplayName)) errors.Add("DisplayName is required.");
        if (string.IsNullOrWhiteSpace(contract.Version)) errors.Add("Version is required.");
        if (contract.Requirements.Count == 0) errors.Add("At least one configuration requirement is required.");

        HashSet<string> ids = new(StringComparer.OrdinalIgnoreCase);
        foreach (ConfigurationRequirement requirement in contract.Requirements)
        {
            string requirementName = string.IsNullOrWhiteSpace(requirement.Id) ? "<missing>" : requirement.Id;
            if (string.IsNullOrWhiteSpace(requirement.Id)) errors.Add("Every requirement must have an Id.");
            else if (!ids.Add(requirement.Id)) errors.Add($"Duplicate requirement Id '{requirement.Id}'.");
            if (string.IsNullOrWhiteSpace(requirement.DisplayName)) errors.Add($"Requirement '{requirementName}' must declare DisplayName.");
            if (string.IsNullOrWhiteSpace(requirement.Purpose)) errors.Add($"Requirement '{requirementName}' must declare Purpose.");

            if (requirement.Kind == ConfigurationRequirementKind.Secret)
            {
                if (string.IsNullOrWhiteSpace(requirement.SecretName)) errors.Add($"Secret requirement '{requirementName}' must declare SecretName.");
                if (!string.IsNullOrWhiteSpace(requirement.ConfigurationKey)) errors.Add($"Secret requirement '{requirementName}' cannot declare ConfigurationKey.");
                if (requirement.FingerprintSafe) errors.Add($"Secret requirement '{requirementName}' cannot enable FingerprintSafe.");
                if (!string.IsNullOrWhiteSpace(requirement.Validator) && !string.Equals(requirement.Validator, "secret", StringComparison.OrdinalIgnoreCase))
                    errors.Add($"Secret requirement '{requirementName}' must use the 'secret' validator when a validator is declared.");
            }
            else
            {
                if (string.IsNullOrWhiteSpace(requirement.ConfigurationKey)) errors.Add($"Non-secret requirement '{requirementName}' must declare ConfigurationKey.");
                if (!string.IsNullOrWhiteSpace(requirement.SecretName)) errors.Add($"Non-secret requirement '{requirementName}' cannot declare SecretName.");
                if (string.Equals(requirement.Validator, "secret", StringComparison.OrdinalIgnoreCase)) errors.Add($"Non-secret requirement '{requirementName}' cannot use the 'secret' validator.");
            }

            if (!string.IsNullOrWhiteSpace(requirement.Validator) && !KnownValidators.Contains(requirement.Validator))
                errors.Add($"Requirement '{requirementName}' declares unknown validator '{requirement.Validator}'.");
        }

        return new(errors.Count == 0, errors);
    }
}
