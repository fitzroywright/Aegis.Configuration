using System.Net.Sockets;
using Aegis.Configuration.Application;
using Aegis.Configuration.Domain;

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
    private static readonly TimeSpan timeout = TimeSpan.FromSeconds(10);
    public bool CanValidate(ConfigurationRequirement requirement) => string.Equals(requirement.Validator, "http", StringComparison.OrdinalIgnoreCase);
    public async ValueTask<(bool Success, string Message)> ValidateAsync(ConfigurationRequirement requirement, string value, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)) return (false, "Endpoint URI is invalid.");
        using CancellationTokenSource timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        try
        {
            using HttpRequestMessage request = new(HttpMethod.Get, uri);
            using HttpResponseMessage response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeoutSource.Token);
            return response.IsSuccessStatusCode ? (true, $"Endpoint reachable ({(int)response.StatusCode}).") : (false, $"Endpoint responded with HTTP {(int)response.StatusCode}.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return (false, "Endpoint verification timed out."); }
        catch (HttpRequestException) { return (false, "Endpoint could not be reached."); }
    }
}

public sealed class TcpEndpointRequirementValidator : IConfigurationRequirementValidator
{
    private static readonly TimeSpan timeout = TimeSpan.FromSeconds(10);
    public bool CanValidate(ConfigurationRequirement requirement) => string.Equals(requirement.Validator, "tcp", StringComparison.OrdinalIgnoreCase);
    public async ValueTask<(bool Success, string Message)> ValidateAsync(ConfigurationRequirement requirement, string value, CancellationToken cancellationToken = default)
    {
        string[] parts = value.Split(':', 2, StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]) || !int.TryParse(parts[1], out int port) || port is < 1 or > 65535) return (false, "Expected host:port.");
        using CancellationTokenSource timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        try { using TcpClient client = new(); await client.ConnectAsync(parts[0], port, timeoutSource.Token); return (true, "TCP endpoint reachable."); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return (false, "TCP endpoint verification timed out."); }
        catch (SocketException) { return (false, "TCP endpoint could not be reached."); }
    }
}
