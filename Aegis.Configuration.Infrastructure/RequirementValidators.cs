using Aegis.Configuration.Application;
using Aegis.Configuration.Domain;

namespace Aegis.Configuration.Infrastructure;

// Configuration validators are deliberately syntax-only. Runtime reachability and
// service health checks belong to Aegis.Diagnostics.
public sealed class UriRequirementValidator : IConfigurationRequirementValidator
{
    public bool CanValidate(ConfigurationRequirement requirement) =>
        string.Equals(requirement.Validator, "uri", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(requirement.Validator, "http", StringComparison.OrdinalIgnoreCase);

    public ValueTask<(bool Success, string Message)> ValidateAsync(
        ConfigurationRequirement requirement,
        string value,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        bool valid = Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) &&
                     (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
        return ValueTask.FromResult(valid
            ? (true, "URI format is valid. Runtime reachability is not tested by Configuration.")
            : (false, "Value is not a valid absolute HTTP/HTTPS URI."));
    }
}

public sealed class TcpEndpointRequirementValidator : IConfigurationRequirementValidator
{
    public bool CanValidate(ConfigurationRequirement requirement) =>
        string.Equals(requirement.Validator, "tcp", StringComparison.OrdinalIgnoreCase);

    public ValueTask<(bool Success, string Message)> ValidateAsync(
        ConfigurationRequirement requirement,
        string value,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string[] parts = value.Split(':', 2, StringSplitOptions.TrimEntries);
        bool valid = parts.Length == 2 &&
                     !string.IsNullOrWhiteSpace(parts[0]) &&
                     int.TryParse(parts[1], out int port) && port is >= 1 and <= 65535;
        return ValueTask.FromResult(valid
            ? (true, "Endpoint format is valid. Network connectivity is not tested by Configuration.")
            : (false, "Expected host:port."));
    }
}
