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
            if (!requirement.Required)
            {
                results.Add(new(requirement.Id, requirement.DisplayName, false, ConfigurationState.NotRequired, "Optional requirement."));
                continue;
            }

            string? value = await valueResolver.ResolveAsync(requirement, cancellationToken);
            if (string.IsNullOrWhiteSpace(value))
            {
                results.Add(new(requirement.Id, requirement.DisplayName, true, ConfigurationState.Missing, "Required value is missing."));
                continue;
            }

            IConfigurationRequirementValidator? validator = validators.FirstOrDefault(x => x.CanValidate(requirement));
            if (validator is null)
            {
                results.Add(new(requirement.Id, requirement.DisplayName, true, ConfigurationState.ConfiguredUnverified, "Value is present but has not been operationally verified."));
                continue;
            }

            (bool success, string message) = await validator.ValidateAsync(requirement, value, cancellationToken);
            results.Add(new(
                requirement.Id,
                requirement.DisplayName,
                true,
                success ? ConfigurationState.Verified : ConfigurationState.Invalid,
                message));
        }

        return new(contract.ApplicationId, contract.Version, results);
    }
}
