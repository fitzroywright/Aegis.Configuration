using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Aegis.Configuration.Domain;

namespace Aegis.Configuration.Infrastructure;

public sealed class DirectoryOperationalContractStore(string directoryPath)
{
    private static readonly JsonSerializerOptions jsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public async ValueTask RegisterAsync(ApplicationOperationalRegistration registration, CancellationToken cancellationToken = default)
    {
        if (!DirectoryContractStore.IsSafeApplicationId(registration.Configuration.ApplicationId))
            throw new InvalidDataException("ApplicationId may contain only letters, digits, '.', '-' and '_'.");
        if (!string.Equals(registration.Configuration.ApplicationId, registration.Dependencies.ApplicationId, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(registration.Configuration.ApplicationId, registration.Environment.ApplicationId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("All registration contracts must use the same ApplicationId.");

        string appId = registration.Configuration.ApplicationId;
        Directory.CreateDirectory(directoryPath);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        await WriteAsync($"{appId}.dependency-contract.json", registration.Dependencies with { LastRegisteredAtUtc = now }, cancellationToken);
        await WriteAsync($"{appId}.environment-contract.json", registration.Environment with
        {
            LastRegisteredAtUtc = now,
            Environment = registration.Environment.Environment with { DetectedAtUtc = registration.Environment.Environment.DetectedAtUtc ?? now }
        }, cancellationToken);
    }

    public async ValueTask<ApplicationDependencyContract?> LoadDependenciesAsync(string applicationId, CancellationToken cancellationToken = default)
        => await ReadAsync<ApplicationDependencyContract>($"{applicationId}.dependency-contract.json", cancellationToken);

    public async ValueTask<ApplicationEnvironmentContract?> LoadEnvironmentAsync(string applicationId, CancellationToken cancellationToken = default)
        => await ReadAsync<ApplicationEnvironmentContract>($"{applicationId}.environment-contract.json", cancellationToken);

    public async ValueTask<IReadOnlyList<ApplicationDependencyContract>> LoadAllDependenciesAsync(CancellationToken cancellationToken = default)
        => await ReadAllAsync<ApplicationDependencyContract>("*.dependency-contract.json", cancellationToken);

    public async ValueTask<IReadOnlyList<ApplicationEnvironmentContract>> LoadAllEnvironmentsAsync(CancellationToken cancellationToken = default)
        => await ReadAllAsync<ApplicationEnvironmentContract>("*.environment-contract.json", cancellationToken);

    private async ValueTask WriteAsync<T>(string fileName, T value, CancellationToken cancellationToken)
    {
        byte[] content = JsonSerializer.SerializeToUtf8Bytes(value, jsonOptions);
        string path = Path.Combine(directoryPath, fileName);
        string temporaryPath = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try { await File.WriteAllBytesAsync(temporaryPath, content, cancellationToken); File.Move(temporaryPath, path, true); }
        finally { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
    }

    private async ValueTask<T?> ReadAsync<T>(string fileName, CancellationToken cancellationToken)
    {
        string path = Path.Combine(directoryPath, fileName);
        if (!File.Exists(path)) return default;
        await using FileStream stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<T>(stream, jsonOptions, cancellationToken);
    }

    private async ValueTask<IReadOnlyList<T>> ReadAllAsync<T>(string pattern, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(directoryPath)) return [];
        List<T> values = [];
        foreach (string path in Directory.EnumerateFiles(directoryPath, pattern, SearchOption.TopDirectoryOnly))
        {
            await using FileStream stream = File.OpenRead(path);
            T? value = await JsonSerializer.DeserializeAsync<T>(stream, jsonOptions, cancellationToken);
            if (value is not null) values.Add(value);
        }
        return values;
    }
}
