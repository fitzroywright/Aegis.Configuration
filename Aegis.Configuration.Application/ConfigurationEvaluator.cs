using Aegis.Configuration.Domain;

namespace Aegis.Configuration.Application;

public interface IConfigurationValueResolver
{
    ValueTask<string?> ResolveAsync(ConfigurationRequirement requirement, CancellationToken cancellationToken = default);
}

public sealed class ConfigurationEvaluator(IConfigurationValueResolver valueResolver)
{
    public async ValueTask<ConfigurationEvaluation> EvaluateAsync(
        ApplicationConfigurationContract contract,
        CancellationToken cancellationToken = default)
    {
        List<RequirementEvaluation> results = [];

        foreach (ConfigurationRequirement requirement in contract.Requirements)
        {
            bool configured;
            string? safeDisplayValue = null;

            if (requirement.IsConfigured.HasValue)
            {
                configured = requirement.IsConfigured.Value;
                safeDisplayValue = requirement.Sensitive ? null : requirement.SafeDisplayValue;
            }
            else
            {
                string? value = await valueResolver.ResolveAsync(requirement, cancellationToken);
                configured = !string.IsNullOrWhiteSpace(value);
                safeDisplayValue = requirement.Sensitive ? null : requirement.SafeDisplayValue;
            }

            ConfigurationState state = configured
                ? ConfigurationState.Configured
                : requirement.Required ? ConfigurationState.Missing : ConfigurationState.OptionalNotConfigured;

            string message = state switch
            {
                ConfigurationState.Configured => "Configured.",
                ConfigurationState.Missing => "Required value is missing.",
                _ => "Optional value is not configured."
            };

            results.Add(new(
                requirement.Id,
                requirement.DisplayName,
                requirement.Required,
                state,
                message,
                requirement.Purpose,
                safeDisplayValue,
                requirement.Example,
                requirement.Sensitive));
        }

        return new(contract.ApplicationId, contract.Version, results);
    }
}
