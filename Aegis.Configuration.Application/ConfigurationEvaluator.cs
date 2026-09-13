using Aegis.Configuration.Domain;

namespace Aegis.Configuration.Application;

public sealed class ConfigurationEvaluator
{
    public ValueTask<ConfigurationEvaluation> EvaluateAsync(
        ApplicationConfigurationContract contract,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        RequirementEvaluation[] results = contract.Requirements.Select(requirement =>
        {
            bool configured = requirement.IsConfigured == true;
            ConfigurationState state = configured
                ? ConfigurationState.Configured
                : requirement.Required
                    ? ConfigurationState.Missing
                    : ConfigurationState.OptionalNotConfigured;

            string message = state switch
            {
                ConfigurationState.Configured => "Configured.",
                ConfigurationState.Missing => "Required value is missing.",
                _ => "Optional value is not configured."
            };

            return new RequirementEvaluation(
                requirement.Id,
                requirement.DisplayName,
                requirement.Required,
                state,
                message,
                requirement.Purpose,
                requirement.Sensitive ? null : requirement.SafeDisplayValue,
                requirement.Example,
                requirement.Sensitive);
        }).ToArray();

        return ValueTask.FromResult(new ConfigurationEvaluation(contract.ApplicationId, contract.Version, results));
    }
}
