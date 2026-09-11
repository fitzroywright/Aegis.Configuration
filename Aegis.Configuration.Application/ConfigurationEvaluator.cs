using Aegis.Configuration.Domain;

namespace Aegis.Configuration.Application;

public interface IConfigurationValueResolver
{
    ValueTask<string?> ResolveAsync(ConfigurationRequirement requirement, CancellationToken cancellationToken = default);
}

public interface IConfigurationRequirementValidator
{
    bool CanValidate(ConfigurationRequirement requirement);
    ValueTask<(bool Success, string Message)> ValidateAsync(ConfigurationRequirement requirement, string value, CancellationToken cancellationToken = default);
}

public sealed class ConfigurationEvaluator(
    IConfigurationValueResolver valueResolver,
    IEnumerable<IConfigurationRequirementValidator> validators)
{
    public async ValueTask<ConfigurationEvaluation> EvaluateAsync(
        ApplicationConfigurationContract contract,
        CancellationToken cancellationToken = default)
    {
        List<RequirementEvaluation> results = [];

        foreach (ConfigurationRequirement requirement in contract.Requirements)
        {
            string? value = await valueResolver.ResolveAsync(requirement, cancellationToken);
            if (string.IsNullOrWhiteSpace(value))
            {
                results.Add(new(
                    requirement.Id,
                    requirement.DisplayName,
                    requirement.Required,
                    requirement.Required ? ConfigurationState.Missing : ConfigurationState.NotRequired,
                    requirement.Required ? "Required value is missing." : "Optional value is not configured."));
                continue;
            }

            IConfigurationRequirementValidator? validator = validators.FirstOrDefault(x => x.CanValidate(requirement));
            if (validator is null)
            {
                ConfigurationState state = string.IsNullOrWhiteSpace(requirement.Validator)
                    ? ConfigurationState.ConfiguredUnverified
                    : ConfigurationState.Invalid;
                string validationMessage = string.IsNullOrWhiteSpace(requirement.Validator)
                    ? "Value is present but no validator is declared."
                    : $"Declared validator '{requirement.Validator}' is unavailable.";

                results.Add(new(requirement.Id, requirement.DisplayName, requirement.Required, state, validationMessage));
                continue;
            }

            (bool success, string validationMessage) = await validator.ValidateAsync(requirement, value, cancellationToken);
            results.Add(new(
                requirement.Id,
                requirement.DisplayName,
                requirement.Required,
                success ? ConfigurationState.Verified : ConfigurationState.Invalid,
                validationMessage));
        }

        return new(contract.ApplicationId, contract.Version, results);
    }
}
