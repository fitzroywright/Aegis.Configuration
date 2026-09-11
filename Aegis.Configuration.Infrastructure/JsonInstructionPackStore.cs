using System.Text.Json;
using System.Text.Json.Serialization;
using Aegis.Configuration.Domain;

namespace Aegis.Configuration.Infrastructure;

public sealed class JsonInstructionPackStore
{
    private static readonly JsonSerializerOptions jsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public async ValueTask<ConfigurationInstructionPack?> LoadAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        await using FileStream stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<ConfigurationInstructionPack>(stream, jsonOptions, cancellationToken);
    }
}
