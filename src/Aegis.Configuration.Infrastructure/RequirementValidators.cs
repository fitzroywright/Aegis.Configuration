using System.Net.Sockets;
using Aegis.Configuration.Application;
using Aegis.Configuration.Domain;

namespace Aegis.Configuration.Infrastructure;

public sealed class UriRequirementValidator : IConfigurationRequirementValidator
{
    public bool CanValidate(ConfigurationRequirement requirement)
    {
        return string.Equals(requirement.Validator, "uri", StringComparison.OrdinalIgnoreCase);
    }

    public ValueTask<(bool Success, string Message)> ValidateAsync(
        ConfigurationRequirement requirement,
        string value,
        CancellationToken cancellationToken = default)
    {
        bool valid = Uri.TryCreate(value, UriKind.Absolute, out Uri? uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
        return ValueTask.FromResult(valid
            ? (true, "URI is syntactically valid.")
            : (false, "Value is not a valid absolute HTTP/HTTPS URI."));
    }
}

public sealed class HttpEndpointRequirementValidator(HttpClient httpClient) : IConfigurationRequirementValidator
{
    public bool CanValidate(ConfigurationRequirement requirement)
    {
        return string.Equals(requirement.Validator, "http", StringComparison.OrdinalIgnoreCase);
    }

    public async ValueTask<(bool Success, string Message)> ValidateAsync(
        ConfigurationRequirement requirement,
        string value,
        CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri))
        {
            return (false, "Endpoint URI is invalid.");
        }

        try
        {
            using HttpRequestMessage request = new(HttpMethod.Get, uri);
            using HttpResponseMessage response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            return response.IsSuccessStatusCode
                ? (true, $"Endpoint reachable ({(int)response.StatusCode}).")
                : (false, $"Endpoint responded with HTTP {(int)response.StatusCode}.");
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            return (false, $"Endpoint unreachable: {exception.Message}");
        }
    }
}

public sealed class TcpEndpointRequirementValidator : IConfigurationRequirementValidator
{
    public bool CanValidate(ConfigurationRequirement requirement)
    {
        return string.Equals(requirement.Validator, "tcp", StringComparison.OrdinalIgnoreCase);
    }

    public async ValueTask<(bool Success, string Message)> ValidateAsync(
        ConfigurationRequirement requirement,
        string value,
        CancellationToken cancellationToken = default)
    {
        string[] parts = value.Split(':', 2, StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || !int.TryParse(parts[1], out int port) || port is < 1 or > 65535)
        {
            return (false, "Expected host:port.");
        }

        try
        {
            using TcpClient client = new();
            await client.ConnectAsync(parts[0], port, cancellationToken);
            return (true, "TCP endpoint reachable.");
        }
        catch (Exception exception) when (exception is SocketException or IOException)
        {
            return (false, $"TCP endpoint unreachable: {exception.Message}");
        }
    }
}
