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
            bool explicitlyConfigured = requirement.IsConfigured == true;
            bool effectiveValueAvailable = requirement.EffectiveValueAvailable
                ?? (explicitlyConfigured || requirement.HasDefault);

            ConfigurationState state = ResolveState(requirement, explicitlyConfigured, effectiveValueAvailable);
            string message = state switch
            {
                ConfigurationState.Configured => SourceMessage("Configured", requirement.EffectiveSource),
                ConfigurationState.Default => SourceMessage("Using application default", requirement.EffectiveSource ?? "Runtime/Code Default"),
                ConfigurationState.Disabled => "Optional capability is disabled and does not affect readiness.",
                ConfigurationState.Unknown => "Configuration state could not be verified.",
                ConfigurationState.Missing => "Required value is unresolved and no effective default is available.",
                _ => "Optional value is not configured."
            };

            return new RequirementEvaluation(
                requirement.Id,
                requirement.DisplayName,
                requirement.Required,
                state,
                message,
                requirement.Purpose,
                requirement.Sensitive ? null : requirement.DisplayValue,
                requirement.Example,
                requirement.Sensitive,
                explicitlyConfigured,
                effectiveValueAvailable,
                requirement.EffectiveSource,
                requirement.HasDefault,
                requirement.Sensitive ? null : requirement.DefaultValue,
                requirement.ConditionalOn,
                requirement.VerificationStatus);
        }).ToArray();

        return ValueTask.FromResult(new ConfigurationEvaluation(contract.ApplicationId, contract.Version, results));
    }

    private static ConfigurationState ResolveState(ConfigurationRequirement requirement, bool explicitlyConfigured, bool effectiveValueAvailable)
    {
        if (string.Equals(requirement.ConfigurationState, "Disabled", StringComparison.OrdinalIgnoreCase))
            return requirement.Required ? ConfigurationState.Missing : ConfigurationState.Disabled;
        if (explicitlyConfigured) return ConfigurationState.Configured;
        if (string.Equals(requirement.ConfigurationState, "Unknown", StringComparison.OrdinalIgnoreCase)) return ConfigurationState.Unknown;
        if (requirement.HasDefault && effectiveValueAvailable) return ConfigurationState.Default;
        return requirement.Required ? ConfigurationState.Missing : ConfigurationState.OptionalNotConfigured;
    }

    private static string SourceMessage(string message, string? source)
        => string.IsNullOrWhiteSpace(source) ? message + "." : $"{message} from {source}.";
}

