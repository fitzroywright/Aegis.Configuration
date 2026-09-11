using Aegis.Configuration.Application;
using Aegis.Configuration.Domain;
using Common.Diagnostics;

namespace Aegis.Configuration.Infrastructure;

public sealed class UriRequirementValidator : IConfigurationRequirementValidator
{
    public bool CanValidate(ConfigurationRequirement requirement) => string.Equals(requirement.Validator, "uri", StringComparison.OrdinalIgnoreCase);

    public ValueTask<(bool Success, string Message)> ValidateAsync(ConfigurationRequirement requirement, string value, CancellationToken cancellationToken = default)
    {
        bool valid = Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
        return ValueTask.FromResult(valid ? (true, "URI is syntactically valid.") : (false, "Value is not a valid absolute HTTP/HTTPS URI."));
    }
}

public sealed class HttpEndpointRequirementValidator(HttpClient httpClient) : IConfigurationRequirementValidator
{
    public bool CanValidate(ConfigurationRequirement requirement) => string.Equals(requirement.Validator, "http", StringComparison.OrdinalIgnoreCase);

    public async ValueTask<(bool Success, string Message)> ValidateAsync(ConfigurationRequirement requirement, string value, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            return (false, "Endpoint URI is invalid.");

        HttpEndpointDiagnosticCheck check = new($"configuration:{requirement.Id}", uri, new FixedHttpClientFactory(httpClient));
        DiagnosticResult result = await check.RunAsync(cancellationToken);
        return (result.IsHealthy, result.Message);
    }

    private sealed class FixedHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }
}

public sealed class TcpEndpointRequirementValidator : IConfigurationRequirementValidator
{
    public bool CanValidate(ConfigurationRequirement requirement) => string.Equals(requirement.Validator, "tcp", StringComparison.OrdinalIgnoreCase);

    public async ValueTask<(bool Success, string Message)> ValidateAsync(ConfigurationRequirement requirement, string value, CancellationToken cancellationToken = default)
    {
        string[] parts = value.Split(':', 2, StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]) || !int.TryParse(parts[1], out int port) || port is < 1 or > 65535)
            return (false, "Expected host:port.");

        TcpEndpointDiagnosticCheck check = new($"configuration:{requirement.Id}", parts[0], port);
        DiagnosticResult result = await check.RunAsync(cancellationToken);
        return (result.IsHealthy, result.Message);
    }
}
