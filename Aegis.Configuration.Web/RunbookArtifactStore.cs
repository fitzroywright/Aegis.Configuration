using System.Text;
using System.Text.Json;

public sealed record RunbookArtifact(
    string ApplicationId,
    string FileName,
    DateTimeOffset LastUpdatedUtc,
    int CurrentVersion = 0);

public sealed record RunbookVersion(
    string ApplicationId,
    int Version,
    DateTimeOffset CreatedAtUtc,
    string Author,
    string Source,
    string? Note,
    string FileName);

public sealed class RunbookArtifactStore
{
    private readonly string _directory;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public RunbookArtifactStore(string directory)
    {
        _directory = directory;
    }

    public Task<RunbookArtifact> SaveAsync(
        string applicationId,
        string markdown,
        CancellationToken cancellationToken) =>
        SaveVersionAsync(applicationId, markdown, "system", "Generated", "Generated from authenticated application contract.", cancellationToken);

    public Task<RunbookArtifact> SaveEditAsync(
        string applicationId,
        string markdown,
        string author,
        string? note,
        CancellationToken cancellationToken) =>
        SaveVersionAsync(applicationId, markdown, author, "Edited", note, cancellationToken);

    public async Task<RunbookArtifact?> RestoreAsync(
        string applicationId,
        int version,
        string author,
        CancellationToken cancellationToken)
    {
        RunbookVersion? historical = (await ListVersionsAsync(applicationId, cancellationToken))
            .FirstOrDefault(x => x.Version == version);
        if (historical is null) return null;

        string versionPath = Path.Combine(VersionDirectory(applicationId), historical.FileName);
        if (!File.Exists(versionPath)) return null;

        string markdown = await File.ReadAllTextAsync(versionPath, cancellationToken);
        return await SaveVersionAsync(
            applicationId,
            markdown,
            author,
            "Restored",
            $"Restored from version {version}.",
            cancellationToken);
    }

    private async Task<RunbookArtifact> SaveVersionAsync(
        string applicationId,
        string markdown,
        string author,
        string source,
        string? note,
        CancellationToken cancellationToken)
    {
        ValidateApplicationId(applicationId);
        if (markdown is null) throw new ArgumentNullException(nameof(markdown));

        await _gate.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(_directory);
            string versionDirectory = VersionDirectory(applicationId);
            Directory.CreateDirectory(versionDirectory);
            string currentFileName = applicationId + ".runbook.md";
            string currentPath = Path.Combine(_directory, currentFileName);
            int currentVersion = NextVersion(versionDirectory) - 1;

            if (currentVersion > 0 && File.Exists(currentPath))
            {
                string current = await File.ReadAllTextAsync(currentPath, cancellationToken);
                if (string.Equals(current, markdown, StringComparison.Ordinal))
                    return new RunbookArtifact(applicationId, currentFileName, File.GetLastWriteTimeUtc(currentPath), currentVersion);
            }

            int nextVersion = currentVersion + 1;
            DateTimeOffset now = DateTimeOffset.UtcNow;
            string versionFile = $"{nextVersion:D6}.runbook.md";
            string metadataFile = $"{nextVersion:D6}.json";
            string versionPath = Path.Combine(versionDirectory, versionFile);
            string metadataPath = Path.Combine(versionDirectory, metadataFile);

            await AtomicWriteAsync(versionPath, markdown, cancellationToken);
            RunbookVersion metadata = new(
                applicationId,
                nextVersion,
                now,
                string.IsNullOrWhiteSpace(author) ? "unknown" : author.Trim(),
                source,
                string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
                versionFile);
            await AtomicWriteAsync(
                metadataPath,
                JsonSerializer.Serialize(metadata, JsonOptions),
                cancellationToken);

            await AtomicWriteAsync(currentPath, markdown, cancellationToken);

            return new RunbookArtifact(applicationId, currentFileName, now, nextVersion);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<string?> ReadAsync(
        string applicationId,
        CancellationToken cancellationToken)
    {
        if (!IsSafe(applicationId)) return null;
        string path = Path.Combine(_directory, applicationId + ".runbook.md");
        if (!File.Exists(path)) return null;
        return await File.ReadAllTextAsync(path, cancellationToken);
    }

    public async Task<string?> ReadVersionAsync(
        string applicationId,
        int version,
        CancellationToken cancellationToken)
    {
        if (!IsSafe(applicationId) || version < 1) return null;
        RunbookVersion? metadata = (await ListVersionsAsync(applicationId, cancellationToken))
            .FirstOrDefault(x => x.Version == version);
        if (metadata is null) return null;
        string path = Path.Combine(VersionDirectory(applicationId), metadata.FileName);
        return File.Exists(path) ? await File.ReadAllTextAsync(path, cancellationToken) : null;
    }

    public async Task<IReadOnlyList<RunbookVersion>> ListVersionsAsync(
        string applicationId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsSafe(applicationId)) return [];

        string directory = VersionDirectory(applicationId);
        if (!Directory.Exists(directory)) return [];

        var versions = new List<RunbookVersion>();
        foreach (string path in Directory.EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                string json = await File.ReadAllTextAsync(path, cancellationToken);
                RunbookVersion? item = JsonSerializer.Deserialize<RunbookVersion>(json, JsonOptions);
                if (item is not null) versions.Add(item);
            }
            catch (JsonException)
            {
                // Ignore a corrupt metadata sidecar; the current runbook remains readable.
            }
        }

        return versions
            .OrderByDescending(x => x.Version)
            .ToArray();
    }

    public async Task<IReadOnlyList<RunbookArtifact>> ListAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Directory.Exists(_directory)) return [];

        var result = new List<RunbookArtifact>();
        foreach (string path in Directory.EnumerateFiles(_directory, "*.runbook.md", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string fileName = Path.GetFileName(path);
            string applicationId = fileName[..^".runbook.md".Length];
            int currentVersion = (await ListVersionsAsync(applicationId, cancellationToken))
                .Select(x => x.Version)
                .DefaultIfEmpty(0)
                .Max();
            result.Add(new RunbookArtifact(
                applicationId,
                fileName,
                File.GetLastWriteTimeUtc(path),
                currentVersion));
        }

        return result
            .OrderBy(x => x.ApplicationId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private string VersionDirectory(string applicationId) =>
        Path.Combine(_directory, "versions", applicationId);

    private static int NextVersion(string versionDirectory)
    {
        int max = 0;
        foreach (string path in Directory.EnumerateFiles(versionDirectory, "*.json", SearchOption.TopDirectoryOnly))
        {
            string name = Path.GetFileNameWithoutExtension(path);
            if (int.TryParse(name, out int version) && version > max) max = version;
        }
        return max + 1;
    }

    private static async Task AtomicWriteAsync(
        string path,
        string content,
        CancellationToken cancellationToken)
    {
        string temporaryPath = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await File.WriteAllTextAsync(
                temporaryPath,
                content,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                cancellationToken);
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private static bool IsSafe(string applicationId) =>
        Aegis.Configuration.Infrastructure.DirectoryContractStore.IsSafeApplicationId(applicationId);

    private static void ValidateApplicationId(string applicationId)
    {
        if (!IsSafe(applicationId))
            throw new InvalidDataException("ApplicationId may contain only letters, digits, '.', '-' and '_'.");
    }
}
