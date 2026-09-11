using System.Text.Json;
using Aegis.Configuration.Domain;

namespace Aegis.Configuration.Infrastructure;

public sealed class JsonContractStore
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public async ValueTask<ApplicationConfigurationContract> LoadAsync(
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        ApplicationConfigurationContract? contract = await JsonSerializer.DeserializeAsync<ApplicationConfigurationContract>(stream, Options, cancellationToken);
        return contract ?? throw new InvalidDataException("Configuration contract is empty or invalid.");
    }
}
