using Common.Diagnostics;
using Npgsql;
using Aegis.Configuration.Application;
using Aegis.Configuration.Domain;

namespace Aegis.Configuration.Web;

internal sealed class ConfigurationPublicUrlDiagnosticLevelTest(IConfiguration configuration) : IDiagnosticLevelLocalTest
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

internal sealed class ConfigurationOperationsUrlDiagnosticLevelTest(IConfiguration configuration) : IDiagnosticLevelLocalTest
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

internal sealed class ConfigurationRegistrationDatabaseConfigDiagnosticLevelTest(IConfiguration configuration) : IDiagnosticLevelLocalTest
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

internal sealed class ConfigurationContractDirectoryDiagnosticLevelTest(IConfiguration configuration, IWebHostEnvironment environment) : IDiagnosticLevelLocalTest
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

internal sealed class ConfigurationContractStoreReadDiagnosticLevelTest(IConfigurationContractStore store) : IDiagnosticLevelLocalTest
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

internal sealed class ConfigurationRegistrationAuthorityReadDiagnosticLevelTest(LifecycleRegistrationAuthority authority) : IDiagnosticLevelLocalTest
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


internal sealed class ConfigurationRegistrationConsistencyDiagnosticLevelTest(LifecycleRegistrationAuthority authority) : IDiagnosticLevelLocalTest
{
    public string TestId => "AEGIS.CONFIGURATION.L2.REGISTRATION.CONSISTENCY";
    public string Name => "Registration authority identities internally consistent";
    public string Owner => "Aegis.Configuration";
    public EngineeringDiagnosticLevel Level => EngineeringDiagnosticLevel.Level2Repair;
    public bool IsDestructive => false;

    public async Task<EngineeringDiagnosticCheckResult> RunAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<LifecycleRegistrationRecord> records = await authority.ListAsync(cancellationToken).ConfigureAwait(false);
        List<string> issues = [];

        string[] duplicateRegistrationIds = records
            .Where(x => !string.IsNullOrWhiteSpace(x.RegistrationId))
            .GroupBy(x => x.RegistrationId, StringComparer.OrdinalIgnoreCase)
            .Where(x => x.Count() > 1)
            .Select(x => x.Key)
            .ToArray();
        if (duplicateRegistrationIds.Length > 0)
            issues.Add($"DuplicateRegistrationIds={duplicateRegistrationIds.Length}");

        string[] duplicateActiveIdentities = records
            .Where(x => x.State != LifecycleRegistrationState.Purged)
            .GroupBy(x => $"{x.ApplicationId}\u001f{x.InstanceId}", StringComparer.OrdinalIgnoreCase)
            .Where(x => x.Count() > 1)
            .Select(x => x.Key.Replace("\u001f", "/", StringComparison.Ordinal))
            .ToArray();
        if (duplicateActiveIdentities.Length > 0)
            issues.Add($"DuplicateActiveIdentities={duplicateActiveIdentities.Length}");

        if (records.Any(x =>
                string.IsNullOrWhiteSpace(x.ApplicationId) ||
                string.IsNullOrWhiteSpace(x.InstanceId) ||
                string.IsNullOrWhiteSpace(x.InstallationId)))
            issues.Add("IncompleteIdentity");

        string evidence = $"Records={records.Count}; Active={records.Count(x => x.State != LifecycleRegistrationState.Purged)}";
        return issues.Count == 0
            ? EngineeringDiagnosticPolicy.Passed(TestId, Name, "Registration authority identities passed consistency checks.", evidence)
            : EngineeringDiagnosticPolicy.Failed(TestId, Name, "Registration authority identity consistency problems were found.", $"{evidence}; Issues={string.Join(";", issues)}");
    }
}

internal sealed class ConfigurationRegistrationAuditDiagnosticLevelTest(LifecycleRegistrationAuthority authority) : IDiagnosticLevelLocalTest
{
    public string TestId => "AEGIS.CONFIGURATION.L1.REGISTRATION.AUDIT_CHAIN";
    public string Name => "Registration lifecycle audit chain valid";
    public string Owner => "Aegis.Configuration";
    public EngineeringDiagnosticLevel Level => EngineeringDiagnosticLevel.Level1CriticalIntervention;
    public bool IsDestructive => false;

    public async Task<EngineeringDiagnosticCheckResult> RunAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<LifecycleRegistrationRecord> records = await authority.ListAsync(cancellationToken).ConfigureAwait(false);
        List<string> invalid = [];

        foreach (LifecycleRegistrationRecord record in records)
        {
            if (record.CreatedAtUtc > record.UpdatedAtUtc)
            {
                invalid.Add(record.RegistrationId);
                continue;
            }

            if (record.Audit is null ||
                record.Audit.Any(x =>
                    x.AtUtc < record.CreatedAtUtc.AddMinutes(-1) ||
                    x.AtUtc > DateTimeOffset.UtcNow.AddMinutes(5) ||
                    string.IsNullOrWhiteSpace(x.Action) ||
                    string.IsNullOrWhiteSpace(x.Actor)))
            {
                invalid.Add(record.RegistrationId);
            }
        }

        string evidence = $"Records={records.Count}; InvalidAuditChains={invalid.Count}";
        return invalid.Count == 0
            ? EngineeringDiagnosticPolicy.Passed(TestId, Name, "Registration lifecycle audit chains passed exhaustive structural validation.", evidence)
            : EngineeringDiagnosticPolicy.Failed(TestId, Name, "One or more registration lifecycle audit chains are structurally invalid.", $"{evidence}; RegistrationIds={string.Join(",", invalid.Take(20))}");
    }
}
