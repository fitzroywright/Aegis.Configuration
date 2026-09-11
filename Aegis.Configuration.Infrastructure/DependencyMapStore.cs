using System.Text.Json;
using System.Text.Json.Serialization;
using Aegis.Configuration.Domain;

namespace Aegis.Configuration.Infrastructure;

public sealed class DependencyMapStore(string path)
{
    private static readonly JsonSerializerOptions options = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    public async ValueTask<IReadOnlyList<ApplicationDependencyMap>> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path)) return [];
        await using FileStream stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<ApplicationDependencyMap[]>(stream, options, cancellationToken) ?? [];
    }
}
