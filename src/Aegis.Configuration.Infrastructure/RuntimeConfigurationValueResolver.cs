using Aegis.Configuration.Application;
using Aegis.Configuration.Domain;
using Microsoft.Extensions.Configuration;

namespace Aegis.Configuration.Infrastructure;

public interface ISecretValueResolver
{
    ValueTask<string?> ResolveAsync(string secretName, CancellationToken cancellationToken = default);
}

public sealed class NullSecretValueResolver : ISecretValueResolver
{
    public ValueTask<string?> ResolveAsync(string secretName, CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult<string?>(null);
    }
}

public sealed class RuntimeConfigurationValueResolver(
    IConfiguration configuration,
    ISecretValueResolver secretResolver) : IConfigurationValueResolver
{
    public async ValueTask<string?> ResolveAsync(
        ConfigurationRequirement requirement,
        CancellationToken cancellationToken = default)
    {
        if (requirement.Kind == ConfigurationRequirementKind.Secret)
        {
            return string.IsNullOrWhiteSpace(requirement.SecretName)
                ? null
                : await secretResolver.ResolveAsync(requirement.SecretName, cancellationToken);
        }

        return string.IsNullOrWhiteSpace(requirement.ConfigurationKey)
            ? null
            : configuration[requirement.ConfigurationKey];
    }
}
