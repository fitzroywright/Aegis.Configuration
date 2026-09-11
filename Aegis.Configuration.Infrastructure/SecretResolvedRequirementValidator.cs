using Aegis.Configuration.Application;
using Aegis.Configuration.Domain;

namespace Aegis.Configuration.Infrastructure;

public sealed class SecretResolvedRequirementValidator : IConfigurationRequirementValidator
{
    public bool CanValidate(ConfigurationRequirement requirement) =>
        requirement.Kind == ConfigurationRequirementKind.Secret &&
        string.Equals(requirement.Validator, "secret", StringComparison.OrdinalIgnoreCase);

    public ValueTask<(bool Success, string Message)> ValidateAsync(
        ConfigurationRequirement requirement,
        string value,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        bool resolved = !string.IsNullOrWhiteSpace(value);
        return ValueTask.FromResult(resolved
            ? (true, "Secret resolved successfully. Value was not inspected, logged or returned.")
            : (false, "Secret could not be resolved."));
    }
}
