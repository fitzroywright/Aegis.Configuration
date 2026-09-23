using Common.Diagnostics;
using Npgsql;
using Aegis.Configuration.Application;
using Aegis.Configuration.Domain;

namespace Aegis.Configuration.Web;

internal sealed class ConfigurationPublicUrlLevelXTest(IConfiguration configuration) : ILevelXLocalTest
{
    public string TestId => "AEGIS.CONFIGURATION.L5.CONFIG.PUBLIC_URL";
    public string Name => "Configuration public URL valid";
    public string Owner => "Aegis.Configuration";
    public EngineeringDiagnosticLevel Level => EngineeringDiagnosticLevel.Level5Scan;
    public bool IsDestructive => false;

    public Task<EngineeringDiagnosticCheckResult> RunAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string? value = configuration["Aegis:PublicUrl"]?.Trim();
        if (string.IsNullOrWhiteSpace(value))
            return Task.FromResult(EngineeringDiagnosticPolicy.Failed(TestId, Name, "Public URL is missing."));
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri))
            return Task.FromResult(EngineeringDiagnosticPolicy.Failed(TestId, Name, "Public URL is not an absolute URI."));
        if (uri.Scheme != Uri.UriSchemeHttps && !uri.IsLoopback)
            return Task.FromResult(EngineeringDiagnosticPolicy.Failed(TestId, Name, "Public URL must use HTTPS except for loopback development."));
        return Task.FromResult(EngineeringDiagnosticPolicy.Passed(TestId, Name, "Public URL is valid.", $"Scheme={uri.Scheme}; Host={uri.Host}"));
    }
}

internal sealed class ConfigurationOperationsUrlLevelXTest(IConfiguration configuration) : ILevelXLocalTest
{
    public string TestId => "AEGIS.CONFIGURATION.L5.CONFIG.OPERATIONS_URL";
    public string Name => "Operations endpoint URL valid";
    public string Owner => "Aegis.Configuration";
    public EngineeringDiagnosticLevel Level => EngineeringDiagnosticLevel.Level5Scan;
    public bool IsDestructive => false;

    public Task<EngineeringDiagnosticCheckResult> RunAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string? value = configuration["Aegis:Operations:Url"]?.Trim();
        if (string.IsNullOrWhiteSpace(value) || !Uri.TryCreate(value, UriKind.Absolute, out Uri? uri))
            return Task.FromResult(EngineeringDiagnosticPolicy.Failed(TestId, Name, "Operations endpoint URL is missing or invalid."));
        return Task.FromResult(EngineeringDiagnosticPolicy.Passed(TestId, Name, "Operations endpoint URL is valid.", $"Scheme={uri.Scheme}; Host={uri.Host}"));
    }
}

internal sealed class ConfigurationRegistrationDatabaseConfigLevelXTest(IConfiguration configuration) : ILevelXLocalTest
{
    public string TestId => "AEGIS.CONFIGURATION.L5.DB.REGISTRATION_CONFIG";
    public string Name => "Registration database configuration valid";
    public string Owner => "Aegis.Configuration";
    public EngineeringDiagnosticLevel Level => EngineeringDiagnosticLevel.Level5Scan;
    public bool IsDestructive => false;

    public Task<EngineeringDiagnosticCheckResult> RunAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string? value = configuration["AegisConfiguration:RegistrationConnectionString"]
            ?? configuration.GetConnectionString("AegisConfiguration");
        if (string.IsNullOrWhiteSpace(value))
            return Task.FromResult(EngineeringDiagnosticPolicy.Failed(TestId, Name, "Registration database connection string is not configured."));
        try
        {
            var parsed = new NpgsqlConnectionStringBuilder(value);
            bool valid = !string.IsNullOrWhiteSpace(parsed.Host) && !string.IsNullOrWhiteSpace(parsed.Database);
            return Task.FromResult(valid
                ? EngineeringDiagnosticPolicy.Passed(TestId, Name, "Registration database connection string is structurally valid.", $"HostConfigured=true; DatabaseConfigured=true; Port={parsed.Port}")
                : EngineeringDiagnosticPolicy.Failed(TestId, Name, "Registration database connection string is incomplete."));
        }
        catch (ArgumentException)
        {
            return Task.FromResult(EngineeringDiagnosticPolicy.Failed(TestId, Name, "Registration database connection string is malformed."));
        }
    }
}

internal sealed class ConfigurationContractDirectoryLevelXTest(IConfiguration configuration, IWebHostEnvironment environment) : ILevelXLocalTest
{
    public string TestId => "AEGIS.CONFIGURATION.L4.STORAGE.CONTRACT_DIRECTORY";
    public string Name => "Configuration contract directory readable";
    public string Owner => "Aegis.Configuration";
    public EngineeringDiagnosticLevel Level => EngineeringDiagnosticLevel.Level4Analysis;
    public bool IsDestructive => false;

    public Task<EngineeringDiagnosticCheckResult> RunAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string path = configuration["AegisConfiguration:ContractDirectory"]
            ?? "/var/lib/aegis/configuration/contracts";
        if (OperatingSystem.IsWindows() && path.StartsWith("/", StringComparison.Ordinal))
            path = Path.Combine(environment.ContentRootPath, "data", "contracts");
        try
        {
            string full = Path.GetFullPath(path);
            if (!Directory.Exists(full))
                return Task.FromResult(EngineeringDiagnosticPolicy.Failed(TestId, Name, "Contract directory does not exist.", $"Path={full}"));
            _ = Directory.EnumerateFileSystemEntries(full).Take(1).ToArray();
            return Task.FromResult(EngineeringDiagnosticPolicy.Passed(TestId, Name, "Contract directory exists and is readable.", $"Path={full}"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return Task.FromResult(EngineeringDiagnosticPolicy.Failed(TestId, Name, "Contract directory cannot be read.", $"FailureType={ex.GetType().Name}"));
        }
    }
}

internal sealed class ConfigurationContractStoreReadLevelXTest(IConfigurationContractStore store) : ILevelXLocalTest
{
    public string TestId => "AEGIS.CONFIGURATION.L4.CONTRACTS.LOAD";
    public string Name => "Configuration contracts load successfully";
    public string Owner => "Aegis.Configuration";
    public EngineeringDiagnosticLevel Level => EngineeringDiagnosticLevel.Level4Analysis;
    public bool IsDestructive => false;

    public async Task<EngineeringDiagnosticCheckResult> RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            IReadOnlyList<ApplicationConfigurationContract> contracts = await store.LoadAllAsync(cancellationToken);
            return EngineeringDiagnosticPolicy.Passed(TestId, Name, "Configuration contracts loaded successfully.", $"Count={contracts.Count}");
        }
        catch (Exception ex)
        {
            return EngineeringDiagnosticPolicy.Failed(TestId, Name, "Configuration contracts could not be loaded.", $"FailureType={ex.GetType().Name}");
        }
    }
}

internal sealed class ConfigurationRegistrationAuthorityReadLevelXTest(LifecycleRegistrationAuthority authority) : ILevelXLocalTest
{
    public string TestId => "AEGIS.CONFIGURATION.L3.REGISTRATION.AUTHORITY_READ";
    public string Name => "Registration authority persistence can enumerate records";
    public string Owner => "Aegis.Configuration";
    public EngineeringDiagnosticLevel Level => EngineeringDiagnosticLevel.Level3Verification;
    public bool IsDestructive => true;

    public async Task<EngineeringDiagnosticCheckResult> RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            IReadOnlyList<LifecycleRegistrationRecord> records = await authority.ListAsync(cancellationToken);
            return EngineeringDiagnosticPolicy.Passed(TestId, Name, "Registration authority persistence is accessible.", $"RecordCount={records.Count}");
        }
        catch (Exception ex)
        {
            return EngineeringDiagnosticPolicy.Failed(TestId, Name, "Registration authority persistence is unavailable.", $"FailureType={ex.GetType().Name}");
        }
    }
}
