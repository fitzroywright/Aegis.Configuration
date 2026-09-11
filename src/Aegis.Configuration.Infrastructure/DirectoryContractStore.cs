using System.Text.Json;
using System.Text.Json.Serialization;
using Aegis.Configuration.Application;
using Aegis.Configuration.Domain;

namespace Aegis.Configuration.Infrastructure;

public sealed class DirectoryContractStore(string directoryPath) : IConfigurationContractStore
{
    private static readonly JsonSerializerOptions jsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public async ValueTask<IReadOnlyList<ApplicationConfigurationContract>> LoadAllAsync(
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(directoryPath))
        {
            return [];
        }

        List<ApplicationConfigurationContract> contracts = [];
        foreach (string path in Directory.EnumerateFiles(directoryPath, "*.configuration-contract.json", SearchOption.TopDirectoryOnly))
        {
            await using FileStream stream = File.OpenRead(path);
            ApplicationConfigurationContract? contract = await JsonSerializer.DeserializeAsync<ApplicationConfigurationContract>(stream, jsonOptions, cancellationToken);
            if (contract is not null)
            {
                contracts.Add(contract);
            }
        }

        return contracts;
    }
}
