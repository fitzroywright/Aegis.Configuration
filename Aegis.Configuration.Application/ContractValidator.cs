using Aegis.Configuration.Domain;

namespace Aegis.Configuration.Application;

public sealed record ContractValidationResult(bool IsValid, IReadOnlyList<string> Errors);

public sealed class ContractValidator
{
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
            if (!requirement.IsConfigured.HasValue) errors.Add($"Requirement '{requirementName}' must publish IsConfigured.");

            if (requirement.Sensitive && !string.IsNullOrWhiteSpace(requirement.SafeDisplayValue))
                errors.Add($"Sensitive requirement '{requirementName}' cannot publish a display value.");

            if (requirement.Kind == ConfigurationRequirementKind.Secret && !string.IsNullOrWhiteSpace(requirement.ConfigurationKey))
                errors.Add($"Secret requirement '{requirementName}' cannot declare ConfigurationKey.");
        }

        return new(errors.Count == 0, errors);
    }
}
