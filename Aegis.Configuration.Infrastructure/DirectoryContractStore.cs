using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Aegis.Configuration.Application;
using Aegis.Configuration.Domain;

namespace Aegis.Configuration.Infrastructure;

public sealed class DirectoryContractStore(string directoryPath) : IConfigurationContractStore, IConfigurationContractRegistrationStore
{
    private const string RetiredCafeteriaApplicationId = "Cafeteria.Services";
    private static readonly JsonSerializerOptions jsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
    private readonly ContractValidator validator = new();

    public async ValueTask<IReadOnlyList<ApplicationConfigurationContract>> LoadAllAsync(CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(directoryPath)) return [];
        List<ApplicationConfigurationContract> contracts = [];
        foreach (string path in Directory.EnumerateFiles(directoryPath, "*.json", SearchOption.TopDirectoryOnly))
        {
            await using FileStream stream = File.OpenRead(path);
            ApplicationConfigurationContract? contract;
            try { contract = await JsonSerializer.DeserializeAsync<ApplicationConfigurationContract>(stream, jsonOptions, cancellationToken); }
            catch (JsonException exception) { throw new InvalidDataException($"Configuration contract '{Path.GetFileName(path)}' contains invalid JSON.", exception); }
            if (contract is null) throw new InvalidDataException($"Configuration contract '{Path.GetFileName(path)}' is empty or invalid.");

            // Aegis.Cafeteria.Services owns the Cafeteria contract. Older releases briefly registered a smaller
            // contract as Cafeteria.Services. Keep accepting that old startup call during rolling upgrades, but
            // never expose it as a second application or allow it to become configuration authority.
            if (IsRetiredPublisher(contract.ApplicationId)) continue;

            ContractValidationResult result = validator.Validate(contract);
            if (!result.IsValid) throw new InvalidDataException($"Configuration contract '{Path.GetFileName(path)}' is invalid: {string.Join(" ", result.Errors)}");
            contracts.Add(contract);
        }
        return contracts;
    }

    public async ValueTask<ConfigurationContractRegistrationResult> RegisterAsync(ApplicationConfigurationContract contract, CancellationToken cancellationToken = default)
    {
        ContractValidationResult validation = validator.Validate(contract);
        if (!validation.IsValid) throw new InvalidDataException(string.Join(" ", validation.Errors));

        if (IsRetiredPublisher(contract.ApplicationId))
        {
            byte[] retiredSemanticContent = SemanticBytes(contract);
            string retiredHash = Convert.ToHexString(SHA256.HashData(retiredSemanticContent));
            return new(contract.ApplicationId, contract.Version, false, retiredHash, DateTimeOffset.UtcNow);
        }

        string safeApplicationId = EnsureSafeApplicationId(contract.ApplicationId);
        Directory.CreateDirectory(directoryPath);
        string path = Path.Combine(directoryPath, $"{safeApplicationId}.configuration-contract.json");

        // The server, not the application, owns registration time. Every authenticated successful registration is a heartbeat.
        // RuntimeEnvironment is observational telemetry, not contract semantics, so changing free disk/memory/IP data does not
        // falsely report that the application's configuration contract changed.
        ApplicationConfigurationContract stamped = contract with { LastRegisteredAtUtc = DateTimeOffset.UtcNow };
        byte[] semanticContent = SemanticBytes(contract);
        string hash = Convert.ToHexString(SHA256.HashData(semanticContent));
        bool changed = true;
        if (File.Exists(path))
        {
            byte[] existing = await File.ReadAllBytesAsync(path, cancellationToken);
            try
            {
                ApplicationConfigurationContract? previous = JsonSerializer.Deserialize<ApplicationConfigurationContract>(existing, jsonOptions);
                if (previous is not null)
                {
                    byte[] previousSemantic = SemanticBytes(previous);
                    changed = !CryptographicOperations.FixedTimeEquals(SHA256.HashData(previousSemantic), SHA256.HashData(semanticContent));
                }
            }
            catch (JsonException) { changed = true; }
        }

        byte[] content = JsonSerializer.SerializeToUtf8Bytes(stamped, jsonOptions);
        string temporaryPath = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try { await File.WriteAllBytesAsync(temporaryPath, content, cancellationToken); File.Move(temporaryPath, path, true); }
        finally { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
        return new(contract.ApplicationId, contract.Version, changed, hash, stamped.LastRegisteredAtUtc.Value);
    }

    public async ValueTask<bool> RetireAsync(string applicationId, string reason, CancellationToken cancellationToken = default)
    {
        string safeApplicationId = EnsureSafeApplicationId(applicationId);
        string activePath = Path.Combine(directoryPath, $"{safeApplicationId}.configuration-contract.json");
        if (!File.Exists(activePath)) return false;

        string retiredDirectory = Path.Combine(directoryPath, "retired");
        Directory.CreateDirectory(retiredDirectory);
        string retiredPath = Path.Combine(
            retiredDirectory,
            $"{safeApplicationId}.{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}.{SanitizeReason(reason)}.configuration-contract.json");

        await Task.Yield();
        File.Move(activePath, retiredPath, false);
        return true;
    }

    public async ValueTask<bool> PurgeAsync(string applicationId, CancellationToken cancellationToken = default)
    {
        string safeApplicationId = EnsureSafeApplicationId(applicationId);
        bool removed = false;
        string activePath = Path.Combine(directoryPath, $"{safeApplicationId}.configuration-contract.json");
        if (File.Exists(activePath))
        {
            File.Delete(activePath);
            removed = true;
        }

        string retiredDirectory = Path.Combine(directoryPath, "retired");
        if (Directory.Exists(retiredDirectory))
        {
            foreach (string path in Directory.EnumerateFiles(
                retiredDirectory,
                $"{safeApplicationId}.*.configuration-contract.json",
                SearchOption.TopDirectoryOnly))
            {
                File.Delete(path);
                removed = true;
            }
        }

        await Task.Yield();
        return removed;
    }

    public ValueTask<int> CountRetiredAsync(string? reason = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string retiredDirectory = Path.Combine(directoryPath, "retired");
        if (!Directory.Exists(retiredDirectory)) return ValueTask.FromResult(0);

        string pattern = string.IsNullOrWhiteSpace(reason)
            ? "*.configuration-contract.json"
            : $"*.{SanitizeReason(reason)}.configuration-contract.json";
        int count = Directory.EnumerateFiles(retiredDirectory, pattern, SearchOption.TopDirectoryOnly).Count();
        return ValueTask.FromResult(count);
    }

    public async ValueTask<IReadOnlyList<string>> ReconcileAsync(
        IReadOnlyCollection<string> activeApplicationIds,
        CancellationToken cancellationToken = default)
    {
        HashSet<string> active = activeApplicationIds
            .Where(IsSafeApplicationId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        List<string> retired = [];
        if (!Directory.Exists(directoryPath)) return retired;

        foreach (string path in Directory.EnumerateFiles(directoryPath, "*.configuration-contract.json", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using FileStream stream = File.OpenRead(path);
            ApplicationConfigurationContract? contract =
                await JsonSerializer.DeserializeAsync<ApplicationConfigurationContract>(stream, jsonOptions, cancellationToken);
            if (contract is null || IsRetiredPublisher(contract.ApplicationId) || active.Contains(contract.ApplicationId))
                continue;

            if (await RetireAsync(contract.ApplicationId, "orphaned", cancellationToken))
                retired.Add(contract.ApplicationId);
        }

        return retired;
    }

    private static string SanitizeReason(string reason)
    {
        string value = string.IsNullOrWhiteSpace(reason) ? "retired" : reason.Trim().ToLowerInvariant();
        return new string(value.Select(ch => char.IsLetterOrDigit(ch) ? ch : '-').ToArray()).Trim('-');
    }

    private static byte[] SemanticBytes(ApplicationConfigurationContract contract)
        => JsonSerializer.SerializeToUtf8Bytes(contract with { LastRegisteredAtUtc = null, RuntimeEnvironment = null }, jsonOptions);

    public static bool IsSafeApplicationId(string applicationId) => !string.IsNullOrWhiteSpace(applicationId) && applicationId.All(character => char.IsLetterOrDigit(character) || character is '.' or '-' or '_');
    private static bool IsRetiredPublisher(string applicationId) => string.Equals(applicationId, RetiredCafeteriaApplicationId, StringComparison.OrdinalIgnoreCase);
    private static string EnsureSafeApplicationId(string applicationId) { if (!IsSafeApplicationId(applicationId)) throw new InvalidDataException("ApplicationId may contain only letters, digits, '.', '-' and '_'."); return applicationId; }
}
