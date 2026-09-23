using System.Reflection;
using System.Text.Json.Nodes;
using Common.Registration;

internal sealed class ConfigurationRegistrationPublisher(
    IConfiguration configuration,
    IWebHostEnvironment environment,
    ILogger<ConfigurationRegistrationPublisher> logger) : BackgroundService
{
    private const string ApplicationId = "Aegis.Configuration";
    private static readonly TimeSpan PublishInterval = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        string operationsUrl = AegisControlPlaneEndpoints.ResolveInternal(
            configuration,
            AegisControlPlaneService.Operations,
            logger) + "/";
        string instanceId = configuration["Aegis:Registration:InstanceId"]?.Trim() ?? Environment.MachineName;
        string credentialFile = configuration["Aegis:Registration:CredentialFile"]?.Trim()
            ?? "/var/lib/aegis/configuration/registration.key";

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                JsonObject contract = BuildContract(instanceId);
                using HttpClient client = new();
                var registration = new ControlPlaneRegistrationClient(
                    client,
                    new ControlPlaneRegistrationOptions(
                        new Uri(operationsUrl, UriKind.Absolute),
                        ApplicationId,
                        instanceId,
                        credentialFile,
                        TimeSpan.FromSeconds(10)),
                    logger: logger);

                ControlPlaneRegistrationStatus result = await registration.RegisterAsync(contract, stoppingToken);
                if (result.IsRegistered)
                {
                    logger.LogInformation(
                        "Aegis.Configuration control-plane registration is active and its configuration contract was published.");
                }
                else
                {
                    logger.LogWarning(
                        "Aegis.Configuration registration state is {RegistrationState}: {RegistrationError}.",
                        result.State,
                        result.Error ?? "No additional detail.");
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Aegis.Configuration automatic registration attempt failed.");
            }

            try
            {
                await Task.Delay(PublishInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
        }
    }

    private JsonObject BuildContract(string instanceId)
    {
        string? diagnosticSecretName = NullIfBlank(configuration["Aegis:Diagnostics:RequestCredentialSecretName"]);
        bool remoteDiagnosticsConfigured = !string.IsNullOrWhiteSpace(diagnosticSecretName);

        return new JsonObject
        {
            ["applicationId"] = ApplicationId,
            ["displayName"] = "Aegis Configuration",
            ["version"] = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "unknown",
            ["presentation"] = new JsonObject
            {
                ["iconUrl"] = configuration["Aegis:Presentation:IconUrl"],
                ["shortName"] = "Configuration",
                ["accent"] = configuration["Aegis:Presentation:Accent"] ?? "gold"
            },
            ["runbookReference"] = "docs/RUNBOOK.md",
            ["siteId"] = NullIfBlank(configuration["Site:Id"]),
            ["instanceId"] = instanceId,
            ["diagnostics"] = new JsonObject
            {
                ["baseUrl"] = NullIfBlank(configuration["Aegis:PublicUrl"]),
                ["healthPath"] = "/health",
                ["diagnosticsRunPath"] = "/api/engineering/diagnostics/diagnostic-level/run",
                ["diagnosticsRunsPath"] = "/api/engineering/diagnostics/diagnostic-level/history",
                ["supportsRemoteDiagnostics"] = remoteDiagnosticsConfigured,
                ["supportsOperationalTelemetry"] = true,
                ["authenticationScheme"] = "DiagnosticLevel-HMAC-SHA256",
                ["secretName"] = diagnosticSecretName,
                ["supportedLevels"] = new JsonArray(1, 2, 3, 4, 5)
            },
            ["runtimeEnvironment"] = new JsonObject
            {
                ["machineName"] = Environment.MachineName,
                ["hostName"] = Environment.MachineName,
                ["hostingEnvironment"] = environment.EnvironmentName,
                ["operatingSystem"] = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
                ["osArchitecture"] = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString(),
                ["processArchitecture"] = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
                ["frameworkDescription"] = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
                ["processorCount"] = Environment.ProcessorCount
            },
            ["requirements"] = new JsonArray
            {
                Requirement(
                    "public-url",
                    "Public Service URL",
                    "Aegis:PublicUrl",
                    "Canonical externally reachable Configuration URL used by Control Plane health monitoring."),
                Requirement(
                    "operations-url",
                    "Operations URL",
                    "Aegis:Operations:Url",
                    "Local Aegis.Operations endpoint used for registration authority validation."),
                Requirement(
                    "contract-directory",
                    "Contract Directory",
                    "AegisConfiguration:ContractDirectory",
                    "Persistent location for registered application configuration contracts."),
                Requirement(
                    "instruction-directory",
                    "Instruction Directory",
                    "AegisConfiguration:InstructionDirectory",
                    "Location containing configuration instruction packs."),
                new JsonObject
                {
                    ["id"] = "registration-database",
                    ["displayName"] = "Registration Database",
                    ["kind"] = 1,
                    ["required"] = true,
                    ["purpose"] = "PostgreSQL persistence for durable Aegis application identities, credential hashes, reconciliation state and audit history.",
                    ["configurationKey"] = "AegisConfiguration:RegistrationConnectionString",
                    ["sensitive"] = true,
                    ["isConfigured"] =
                        !string.IsNullOrWhiteSpace(configuration["AegisConfiguration:RegistrationConnectionString"]) ||
                        !string.IsNullOrWhiteSpace(configuration.GetConnectionString("AegisConfiguration")),
                    ["configurationState"] =
                        !string.IsNullOrWhiteSpace(configuration["AegisConfiguration:RegistrationConnectionString"]) ||
                        !string.IsNullOrWhiteSpace(configuration.GetConnectionString("AegisConfiguration"))
                            ? "Configured"
                            : "Unresolved",
                    ["effectiveSource"] = "Protected configuration / secret provider"
                }
            }
        };
    }

    private JsonObject Requirement(string id, string name, string key, string purpose)
    {
        string? value = NullIfBlank(configuration[key]);
        return new JsonObject
        {
            ["id"] = id,
            ["displayName"] = name,
            ["kind"] = 0,
            ["required"] = true,
            ["purpose"] = purpose,
            ["configurationKey"] = key,
            ["sensitive"] = false,
            ["isConfigured"] = value is not null,
            ["effectiveValueAvailable"] = value is not null,
            ["configurationState"] = value is not null ? "Configured" : "Unresolved",
            ["effectiveSource"] = value is not null ? "Resolved IConfiguration" : "Unresolved",
            ["displayValue"] = value
        };
    }

    private static string? NullIfBlank(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
