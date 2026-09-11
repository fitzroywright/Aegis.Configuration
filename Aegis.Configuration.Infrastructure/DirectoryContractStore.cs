using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Aegis.Configuration.Application;
using Aegis.Configuration.Domain;

namespace Aegis.Configuration.Infrastructure;

public sealed class DirectoryContractStore(string directoryPath) : IConfigurationContractStore, IConfigurationContractRegistrationStore
{
    private static readonly JsonSerializerOptions jsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

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
            ContractValidationResult result = validator.Validate(contract);
            if (!result.IsValid) throw new InvalidDataException($"Configuration contract '{Path.GetFileName(path)}' is invalid: {string.Join(" ", result.Errors)}");
            contracts.Add(contract);
        }
        return contracts;
    }

    public async ValueTask<ConfigurationContractRegistrationResult> RegisterAsync(
        ApplicationConfigurationContract contract,
        CancellationToken cancellationToken = default)
    {
        ContractValidationResult validation = validator.Validate(contract);
        if (!validation.IsValid)
            throw new InvalidDataException(string.Join(" ", validation.Errors));

        string safeApplicationId = EnsureSafeApplicationId(contract.ApplicationId);
        Directory.CreateDirectory(directoryPath);
        string path = Path.Combine(directoryPath, $"{safeApplicationId}.configuration-contract.json");
        byte[] content = JsonSerializer.SerializeToUtf8Bytes(contract, jsonOptions);
        string hash = Convert.ToHexString(SHA256.HashData(content));

        if (File.Exists(path))
        {
            byte[] existing = await File.ReadAllBytesAsync(path, cancellationToken);
            if (CryptographicOperations.FixedTimeEquals(SHA256.HashData(existing), SHA256.HashData(content)))
                return new(contract.ApplicationId, contract.Version, false, hash);
        }

        string temporaryPath = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await File.WriteAllBytesAsync(temporaryPath, content, cancellationToken);
            File.Move(temporaryPath, path, true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }

        return new(contract.ApplicationId, contract.Version, true, hash);
    }

    public static bool IsSafeApplicationId(string applicationId)
        => !string.IsNullOrWhiteSpace(applicationId) && applicationId.All(character => char.IsLetterOrDigit(character) || character is '.' or '-' or '_');

    private static string EnsureSafeApplicationId(string applicationId)
    {
        if (!IsSafeApplicationId(applicationId))
            throw new InvalidDataException("ApplicationId may contain only letters, digits, '.', '-' and '_'.");
        return applicationId;
    }
}
