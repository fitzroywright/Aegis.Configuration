using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Channels;
using Common.Diagnostics;
using Common.Registration;

internal sealed class RegistrationLifecycleTelemetryPublisher : BackgroundService
{
    private readonly Channel<(LifecycleRegistrationRecord Record, LifecycleAuditEvent Audit)> _queue =
        Channel.CreateBounded<(LifecycleRegistrationRecord, LifecycleAuditEvent)>(
            new BoundedChannelOptions(1024)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.DropOldest
            });
    private const string PublisherApplicationId = "Aegis.Configuration";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOperationsLogCredentialSource _credentialSource;
    private readonly ILogger<RegistrationLifecycleTelemetryPublisher> _logger;
    private readonly Uri _endpoint;
    private readonly string _instanceId;

    public RegistrationLifecycleTelemetryPublisher(
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory,
        ILogger<RegistrationLifecycleTelemetryPublisher> logger)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _instanceId = configuration["Aegis:Registration:InstanceId"]?.Trim()
            ?? Environment.MachineName;

        string credentialFile = configuration["Aegis:Registration:CredentialFile"]?.Trim()
            ?? "/var/lib/aegis/configuration/registration.key";

        _credentialSource = new ControlPlaneOperationsLogCredentialSource(
            credentialFile,
            PublisherApplicationId,
            _instanceId);

        string operationsUrl = AegisControlPlaneEndpoints.ResolveInternal(
            configuration,
            AegisControlPlaneService.Operations,
            _logger);

        _endpoint = new Uri(
            operationsUrl.TrimEnd('/') + "/api/operations/activity/observe",
            UriKind.Absolute);
    }

    public Task PublishAsync(
        LifecycleRegistrationRecord record,
        LifecycleAuditEvent audit,
        CancellationToken cancellationToken)
    {
        if (!_queue.Writer.TryWrite((record, audit)))
        {
            _logger.LogWarning(
                "Registration lifecycle telemetry queue rejected an event. RegistrationId={RegistrationId}, Stage={Stage}.",
                record.RegistrationId,
                audit.Action);
        }

        return Task.CompletedTask;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach ((LifecycleRegistrationRecord Record, LifecycleAuditEvent Audit) item in
            _queue.Reader.ReadAllAsync(stoppingToken))
        {
            await PublishCoreAsync(item.Record, item.Audit, stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task PublishCoreAsync(
        LifecycleRegistrationRecord record,
        LifecycleAuditEvent audit,
        CancellationToken cancellationToken)
    {
        try
        {
            OperationsLogCredential? credential =
                await _credentialSource.GetAsync(cancellationToken).ConfigureAwait(false);

            if (credential is null || string.IsNullOrWhiteSpace(credential.Credential))
            {
                _logger.LogWarning(
                    "Registration lifecycle telemetry was not published because the Aegis.Configuration control-plane credential is unavailable. RegistrationId={RegistrationId}, Stage={Stage}.",
                    record.RegistrationId,
                    audit.Action);
                return;
            }

            LifecycleEvent lifecycleEvent = LifecycleEvent.Create(
                applicationId: record.ApplicationId,
                instanceId: record.InstanceId,
                flow: "RegistrationLifecycle",
                stage: audit.Action,
                outcome: OutcomeFor(audit),
                correlationId: record.RegistrationId,
                operationId: audit.CorrelationId,
                relatedBusinessId: record.RegistrationId,
                code: $"REGISTRATION_{audit.Action.ToUpperInvariant()}",
                detail: DetailFor(audit),
                properties: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["registrationId"] = record.RegistrationId,
                    ["previousState"] = audit.PreviousState?.ToString() ?? "None",
                    ["resultingState"] = audit.ResultingState.ToString(),
                    ["actor"] = audit.Actor
                },
                occurredAtUtc: audit.AtUtc);

            using HttpRequestMessage request = new(HttpMethod.Post, _endpoint)
            {
                Content = JsonContent.Create(lifecycleEvent)
            };

            request.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", credential.Credential);
            request.Headers.TryAddWithoutValidation(
                "X-Aegis-Application-Id",
                credential.ApplicationId);
            request.Headers.TryAddWithoutValidation(
                "X-Aegis-Instance-Id",
                credential.InstanceId);
            request.Headers.TryAddWithoutValidation(
                "X-Aegis-Installation-Id",
                credential.InstallationId);
            request.Headers.TryAddWithoutValidation(
                "X-Aegis-Correlation-Id",
                lifecycleEvent.CorrelationId);

            using HttpResponseMessage response = await _httpClientFactory
                .CreateClient("registration-lifecycle-telemetry")
                .SendAsync(request, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Operations rejected registration lifecycle telemetry. RegistrationId={RegistrationId}, Stage={Stage}, StatusCode={StatusCode}.",
                    record.RegistrationId,
                    audit.Action,
                    (int)response.StatusCode);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // Observability must never turn a successful registration state transition into a failure.
            _logger.LogWarning(
                exception,
                "Registration lifecycle telemetry publication failed after persistence. RegistrationId={RegistrationId}, Stage={Stage}.",
                record.RegistrationId,
                audit.Action);
        }
    }

    internal static LifecycleEventOutcome OutcomeFor(LifecycleAuditEvent audit) =>
        audit.Action switch
        {
            "Rejected" => LifecycleEventOutcome.Failed,
            "IdentityConflict" => LifecycleEventOutcome.Failed,
            "IdentityConflictAuthentication" => LifecycleEventOutcome.Failed,
            "Revoked" => LifecycleEventOutcome.Warning,
            "Purged" => LifecycleEventOutcome.Warning,
            "RecoveryRequested" => LifecycleEventOutcome.Warning,
            "RegistrationRequested" => LifecycleEventOutcome.Started,
            "Approved" => LifecycleEventOutcome.Succeeded,
            "CredentialClaimed" => LifecycleEventOutcome.Succeeded,
            "Authenticated" => LifecycleEventOutcome.Succeeded,
            _ => audit.ResultingState switch
            {
                LifecycleRegistrationState.Rejected or
                LifecycleRegistrationState.IdentityConflict => LifecycleEventOutcome.Failed,
                LifecycleRegistrationState.Revoked or
                LifecycleRegistrationState.Purged or
                LifecycleRegistrationState.RecoveryPending => LifecycleEventOutcome.Warning,
                LifecycleRegistrationState.Pending => LifecycleEventOutcome.Started,
                _ => LifecycleEventOutcome.Succeeded
            }
        };

    private static string DetailFor(LifecycleAuditEvent audit)
    {
        string transition = audit.PreviousState.HasValue
            ? $"{audit.PreviousState} -> {audit.ResultingState}"
            : audit.ResultingState.ToString();

        return $"{audit.Action}: {transition}";
    }
}
