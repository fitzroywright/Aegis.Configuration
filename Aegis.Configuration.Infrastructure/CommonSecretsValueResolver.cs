using Aegis.Configuration.Domain;
using Common.Secrets;

namespace Aegis.Configuration.Infrastructure;

public sealed class CommonSecretsValueResolver(ISecretProvider secretProvider) : ISecretValueResolver
{
    public async ValueTask<string?> ResolveAsync(
        string secretName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(secretName))
        {
            return null;
        }

        return await secretProvider.GetAsync(secretName, cancellationToken);
    }
}
