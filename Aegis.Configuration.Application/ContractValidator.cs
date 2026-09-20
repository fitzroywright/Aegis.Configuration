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
        if (contract.Presentation is not null)
        {
            string? icon = contract.Presentation.IconUrl?.Trim();
            if (!string.IsNullOrWhiteSpace(icon))
            {
                bool safeRelative = icon.StartsWith("/", StringComparison.Ordinal) &&
                    !icon.StartsWith("//", StringComparison.Ordinal) &&
                    !icon.Contains("..", StringComparison.Ordinal);
                bool safeHttps = Uri.TryCreate(icon, UriKind.Absolute, out Uri? uri) &&
                    uri.Scheme == Uri.UriSchemeHttps;
                if (!safeRelative && !safeHttps)
                    errors.Add("Presentation.IconUrl must be an HTTPS URL or a safe root-relative path.");
            }

            if (contract.Presentation.ShortName?.Length > 32)
                errors.Add("Presentation.ShortName cannot exceed 32 characters.");
            if (contract.Presentation.Accent?.Length > 32)
                errors.Add("Presentation.Accent cannot exceed 32 characters.");
        }

        HashSet<string> ids = new(StringComparer.OrdinalIgnoreCase);
        foreach (ConfigurationRequirement requirement in contract.Requirements)
        {
            string requirementName = string.IsNullOrWhiteSpace(requirement.Id) ? "<missing>" : requirement.Id;
            if (string.IsNullOrWhiteSpace(requirement.Id)) errors.Add("Every requirement must have an Id.");
            else if (!ids.Add(requirement.Id)) errors.Add($"Duplicate requirement Id '{requirement.Id}'.");
            if (string.IsNullOrWhiteSpace(requirement.DisplayName)) errors.Add($"Requirement '{requirementName}' must declare DisplayName.");
            if (string.IsNullOrWhiteSpace(requirement.Purpose)) errors.Add($"Requirement '{requirementName}' must declare Purpose.");

            bool explicitlyUnknown = string.Equals(requirement.ConfigurationState, "Unknown", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(requirement.VerificationStatus);
            if (!requirement.IsConfigured.HasValue && !explicitlyUnknown)
                errors.Add($"Requirement '{requirementName}' must publish IsConfigured or explicitly report an unverifiable state.");

            if (requirement.Sensitive && !string.IsNullOrWhiteSpace(requirement.SafeDisplayValue))
                errors.Add($"Sensitive requirement '{requirementName}' cannot publish a display value.");

            if (requirement.Kind == ConfigurationRequirementKind.Secret && !string.IsNullOrWhiteSpace(requirement.ConfigurationKey))
                errors.Add($"Secret requirement '{requirementName}' cannot declare ConfigurationKey.");
        }

        return new(errors.Count == 0, errors);
    }
}
