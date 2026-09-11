using System.Text.Json;
using System.Text.Json.Serialization;
using Aegis.Configuration.Domain;

namespace Aegis.Configuration.Infrastructure;

public sealed class JsonDependencyGraphStore(string path)
{
    private static readonly JsonSerializerOptions options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public async ValueTask<DependencyGraph> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path)) return new([], []);
        await using FileStream stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<DependencyGraph>(stream, options, cancellationToken) ?? new([], []);
    }
}
