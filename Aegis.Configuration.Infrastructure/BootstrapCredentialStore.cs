using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Aegis.Configuration.Infrastructure;

public sealed record BootstrapCredentialCreateRequest(string? Name, int? ExpiresInDays);

public sealed record BootstrapCredentialCreated(
    string Id,
    string Name,
    string Client,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    string Key);

public sealed record BootstrapCredentialSummary(
    string Id,
    string Name,
    string Client,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    DateTimeOffset? LastUsedAtUtc,
    DateTimeOffset? RevokedAtUtc)
{
    public bool IsActive => RevokedAtUtc is null && ExpiresAtUtc > DateTimeOffset.UtcNow;
}

public sealed class BootstrapCredentialStore
{
    private const string OperationsClient = "Aegis.Operations";
    private const string KeyPrefix = "aegis_cfg_";
    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public BootstrapCredentialStore(string path)
    {
        _path = Path.GetFullPath(path);
    }

    public async Task<BootstrapCredentialCreated> CreateAsync(
        BootstrapCredentialCreateRequest request,
        CancellationToken ct = default)
    {
        string name = string.IsNullOrWhiteSpace(request.Name) ? "Operations bootstrap" : request.Name.Trim();
        int days = Math.Clamp(request.ExpiresInDays ?? 90, 1, 365);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        string id = Guid.NewGuid().ToString("N");
        string secret = KeyPrefix + Base64Url(RandomNumberGenerator.GetBytes(32));

        await _gate.WaitAsync(ct);
        try
        {
            List<PersistedCredential> items = await LoadUnsafeAsync(ct);
            items.Add(new PersistedCredential(
                id,
                name,
                OperationsClient,
                Hash(secret),
                now,
                now.AddDays(days),
                null,
                null));
            await SaveUnsafeAsync(items, ct);
        }
        finally
        {
            _gate.Release();
        }

        return new BootstrapCredentialCreated(id, name, OperationsClient, now, now.AddDays(days), secret);
    }

    public async Task<IReadOnlyList<BootstrapCredentialSummary>> ListAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            return (await LoadUnsafeAsync(ct))
                .OrderByDescending(x => x.CreatedAtUtc)
                .Select(ToSummary)
                .ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> ValidateAsync(string supplied, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(supplied))
            return false;

        await _gate.WaitAsync(ct);
        try
        {
            List<PersistedCredential> items = await LoadUnsafeAsync(ct);
            DateTimeOffset now = DateTimeOffset.UtcNow;
            string suppliedHash = Hash(supplied);
            PersistedCredential? match = items.FirstOrDefault(x =>
                x.RevokedAtUtc is null &&
                x.ExpiresAtUtc > now &&
                FixedHashEquals(x.KeyHash, suppliedHash));

            if (match is null)
                return false;

            int index = items.FindIndex(x => x.Id == match.Id);
            items[index] = match with { LastUsedAtUtc = now };
            await SaveUnsafeAsync(items, ct);
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> RevokeAsync(string id, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            List<PersistedCredential> items = await LoadUnsafeAsync(ct);
            int index = items.FindIndex(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
                return false;

            PersistedCredential item = items[index];
            if (item.RevokedAtUtc is null)
            {
                items[index] = item with { RevokedAtUtc = DateTimeOffset.UtcNow };
                await SaveUnsafeAsync(items, ct);
            }

            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<List<PersistedCredential>> LoadUnsafeAsync(CancellationToken ct)
    {
        if (!File.Exists(_path))
            return [];

        await using FileStream stream = new(_path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return await JsonSerializer.DeserializeAsync<List<PersistedCredential>>(stream, _json, ct) ?? [];
    }

    private async Task SaveUnsafeAsync(List<PersistedCredential> items, CancellationToken ct)
    {
        string? directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        string temp = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        await using (FileStream stream = new(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            await JsonSerializer.SerializeAsync(stream, items, _json, ct);
            await stream.FlushAsync(ct);
        }

        File.Move(temp, _path, true);

        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(_path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    private static BootstrapCredentialSummary ToSummary(PersistedCredential item) =>
        new(item.Id, item.Name, item.Client, item.CreatedAtUtc, item.ExpiresAtUtc, item.LastUsedAtUtc, item.RevokedAtUtc);

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static bool FixedHashEquals(string expectedHash, string actualHash)
    {
        byte[] expected = Convert.FromHexString(expectedHash);
        byte[] actual = Convert.FromHexString(actualHash);
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private sealed record PersistedCredential(
        string Id,
        string Name,
        string Client,
        string KeyHash,
        DateTimeOffset CreatedAtUtc,
        DateTimeOffset ExpiresAtUtc,
        DateTimeOffset? LastUsedAtUtc,
        DateTimeOffset? RevokedAtUtc);
}
