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
            // Preferred V1 path: the application publishes whether the value is configured
            // and, when safe, a display value. Configuration does not probe dependencies.
            if (requirement.IsConfigured.HasValue)
            {
                bool configured = requirement.IsConfigured.Value;
                ConfigurationState state = configured
                    ? ConfigurationState.ConfiguredUnverified
                    : requirement.Required ? ConfigurationState.Missing : ConfigurationState.NotRequired;
                string message = configured
                    ? "Configured. Operational validation belongs to Aegis.Diagnostics."
                    : requirement.Required ? "Required value is missing." : "Optional value is not configured.";

                results.Add(new(
                    requirement.Id, requirement.DisplayName, requirement.Required, state, message,
                    requirement.Purpose,
                    requirement.Sensitive ? null : requirement.SafeDisplayValue,
                    requirement.Example,
                    requirement.Sensitive));
                continue;
            }

            // Backward compatibility for existing contracts that publish a configuration key.
            string? value = await valueResolver.ResolveAsync(requirement, cancellationToken);
            bool present = !string.IsNullOrWhiteSpace(value);
            ConfigurationState fallbackState = present
                ? ConfigurationState.ConfiguredUnverified
                : requirement.Required ? ConfigurationState.Missing : ConfigurationState.NotRequired;
            string fallbackMessage = present
                ? "Configured. Operational validation belongs to Aegis.Diagnostics."
                : requirement.Required ? "Required value is missing." : "Optional value is not configured.";

            results.Add(new(
                requirement.Id, requirement.DisplayName, requirement.Required, fallbackState, fallbackMessage,
                requirement.Purpose,
                requirement.Sensitive ? null : requirement.SafeDisplayValue,
                requirement.Example,
                requirement.Sensitive));
        }

        return new(contract.ApplicationId, contract.Version, results);
    }
}
