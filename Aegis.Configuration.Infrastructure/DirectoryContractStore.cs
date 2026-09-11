using System.Text.Json;
using System.Text.Json.Serialization;
using Aegis.Configuration.Application;
using Aegis.Configuration.Domain;

namespace Aegis.Configuration.Infrastructure;

public sealed class DirectoryContractStore(string directoryPath) : IConfigurationContractStore
{
    private static readonly JsonSerializerOptions jsonOptions = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    private readonly ContractValidator validator = new();

    public async ValueTask<IReadOnlyList<ApplicationConfigurationContract>> LoadAllAsync(CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(directoryPath)) return [];
        List<ApplicationConfigurationContract> contracts = [];
        foreach (string path in Directory.EnumerateFiles(directoryPath, "*.configuration-contract.json", SearchOption.TopDirectoryOnly))
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
}
