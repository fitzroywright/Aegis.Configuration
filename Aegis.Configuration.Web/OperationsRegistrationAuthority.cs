using System.Net.Http.Headers;
using Common.Registration;

internal sealed class OperationsRegistrationAuthority(
    IConfiguration configuration,
    IHttpClientFactory httpClientFactory,
    ILogger<OperationsRegistrationAuthority> logger)
{
    public async Task<bool> ValidateOperationsAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        string applicationId = request.Headers["X-Aegis-Application-Id"].ToString().Trim();
        string instanceId = request.Headers["X-Aegis-Instance-Id"].ToString().Trim();
        string credential = request.Headers["X-Aegis-Registration-Key"].ToString().Trim();

        bool supportedControlPlaneCaller =
            string.Equals(applicationId, "Aegis.Operations", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(applicationId, "Aegis.Diagnostics", StringComparison.OrdinalIgnoreCase);

        if (!supportedControlPlaneCaller ||
            string.IsNullOrWhiteSpace(instanceId) ||
            string.IsNullOrWhiteSpace(credential))
        {
            logger.LogWarning(
                "Configuration machine API rejected a request because the control-plane registration identity was incomplete or unsupported. ApplicationId={ApplicationId}.",
                applicationId);
            return false;
        }

        string operationsUrl = AegisControlPlaneEndpoints.ResolveInternal(
            configuration,
            AegisControlPlaneService.Operations,
            logger);
        using HttpClient client = httpClientFactory.CreateClient("operations-registration");
        using var validationRequest = new HttpRequestMessage(
            HttpMethod.Post,
            operationsUrl + "/api/registration/validate");
        validationRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential);
        validationRequest.Headers.TryAddWithoutValidation("X-Aegis-Application-Id", applicationId);
        validationRequest.Headers.TryAddWithoutValidation("X-Aegis-Instance-Id", instanceId);

        try
        {
            using HttpResponseMessage response = await client.SendAsync(validationRequest, cancellationToken);
            if (response.IsSuccessStatusCode)
                return true;

            logger.LogWarning(
                "Operations rejected Configuration machine API authentication for {ApplicationId}/{InstanceId} with HTTP {StatusCode}.",
                applicationId,
                instanceId,
                (int)response.StatusCode);
            return false;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(exception, "Unable to validate the Operations registration credential.");
            return false;
        }
    }
}
