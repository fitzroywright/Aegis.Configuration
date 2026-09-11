using System.Text.Json;
using System.Text.Json.Serialization;
using Aegis.Configuration.Application;

namespace Aegis.Configuration.Infrastructure;

public sealed class JsonCommissioningHistoryStore(string path) : ICommissioningHistoryStore
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private static readonly JsonSerializerOptions jsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = false, Converters = { new JsonStringEnumConverter() } };

    public async ValueTask AppendAsync(CommissioningHistoryEntry entry, CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            string? directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
            await using FileStream stream = new(path, FileMode.Append, FileAccess.Write, FileShare.Read);
            await JsonSerializer.SerializeAsync(stream, entry, jsonOptions, cancellationToken);
            await stream.WriteAsync("\n"u8.ToArray(), cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }
        finally { gate.Release(); }
    }

    public async ValueTask<IReadOnlyList<CommissioningHistoryEntry>> ReadAsync(string? applicationId = null, CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(path)) return [];
            List<CommissioningHistoryEntry> entries = [];
            foreach (string line in await File.ReadAllLinesAsync(path, cancellationToken))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    CommissioningHistoryEntry? entry = JsonSerializer.Deserialize<CommissioningHistoryEntry>(line, jsonOptions);
                    if (entry is not null && (applicationId is null || string.Equals(entry.ApplicationId, applicationId, StringComparison.OrdinalIgnoreCase))) entries.Add(entry);
                }
                catch (JsonException) { /* Ignore a damaged trailing/legacy record; valid history remains readable. */ }
            }
            return entries.OrderByDescending(x => x.RecordedAtUtc).ToArray();
        }
        finally { gate.Release(); }
    }
}
