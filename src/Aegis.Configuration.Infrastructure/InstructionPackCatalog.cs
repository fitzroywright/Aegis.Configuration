using System.Text.Json;
using System.Text.Json.Serialization;
using Aegis.Configuration.Domain;

namespace Aegis.Configuration.Infrastructure;

public sealed class InstructionPackCatalog(string rootPath)
{
    private static readonly JsonSerializerOptions jsonOptions = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public async ValueTask<ConfigurationInstructionPack?> FindAsync(string applicationId, string applicationVersion, CancellationToken cancellationToken = default)
    {
        string applicationPath = Path.Combine(rootPath, applicationId);
        if (!Directory.Exists(applicationPath)) return null;
        List<ConfigurationInstructionPack> candidates = [];
        foreach (string file in Directory.EnumerateFiles(applicationPath, "*.json", SearchOption.AllDirectories))
        {
            await using FileStream stream = File.OpenRead(file);
            ConfigurationInstructionPack? pack = await JsonSerializer.DeserializeAsync<ConfigurationInstructionPack>(stream, jsonOptions, cancellationToken);
            if (pack is not null && string.Equals(pack.ApplicationId, applicationId, StringComparison.OrdinalIgnoreCase) && string.Equals(pack.ApplicationVersion, applicationVersion, StringComparison.OrdinalIgnoreCase)) candidates.Add(pack);
        }
        return candidates.OrderByDescending(x => ParseVersion(x.PackVersion)).FirstOrDefault();
    }

    private static Version ParseVersion(string value) => Version.TryParse(value, out Version? version) ? version : new Version(0, 0);
}
