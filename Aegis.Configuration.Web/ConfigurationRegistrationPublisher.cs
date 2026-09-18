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
        string operationsUrl = (configuration["Aegis:Operations:Url"] ?? "http://127.0.0.1:5100").TrimEnd('/') + "/";
        string instanceId = configuration["Aegis:Registration:InstanceId"]?.Trim() ?? Environment.MachineName;
        string credentialFile = configuration["Aegis:Registration:CredentialFile"]?.Trim()
            ?? "/var/lib/aegis/configuration/registration.key";

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                JsonObject contract = BuildContract(instanceId);
                using HttpClient client = new();
                var registration = new ApplicationRegistrationClient(
                    client,
                    new ApplicationRegistrationOptions(
                        new Uri(operationsUrl, UriKind.Absolute),
                        ApplicationId,
                        instanceId,
                        RegistrationCredentialResolver.EnvironmentVariableName,
                        TimeSpan.FromSeconds(10),
                        AutoProvisionControlPlane: true,
                        CredentialFilePath: credentialFile),
                    logger: logger);

                ApplicationRegistrationStatus result = await registration.RegisterAsync(contract, stoppingToken);
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
        return new JsonObject
        {
            ["applicationId"] = ApplicationId,
            ["displayName"] = "Aegis Configuration",
            ["version"] = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "unknown",
            ["siteId"] = NullIfBlank(configuration["Site:Id"]),
            ["instanceId"] = instanceId,
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
                    "Location containing configuration instruction packs.")
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
            ["safeDisplayValue"] = value
        };
    }

    private static string? NullIfBlank(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
