using System.Text;

internal sealed record RunbookArtifact(
    string ApplicationId,
    string FileName,
    DateTimeOffset LastUpdatedUtc);

internal sealed class RunbookArtifactStore
{
    private readonly string _directory;

    public RunbookArtifactStore(string directory)
    {
        _directory = directory;
    }

    public async Task<RunbookArtifact> SaveAsync(
        string applicationId,
        string markdown,
        CancellationToken cancellationToken)
    {
        if (!Aegis.Configuration.Infrastructure.DirectoryContractStore.IsSafeApplicationId(applicationId))
            throw new InvalidDataException("ApplicationId may contain only letters, digits, '.', '-' and '_'.");

        Directory.CreateDirectory(_directory);
        string fileName = applicationId + ".runbook.md";
        string path = Path.Combine(_directory, fileName);
        string temporaryPath = path + ".tmp-" + Guid.NewGuid().ToString("N");

        try
        {
            await File.WriteAllTextAsync(
                temporaryPath,
                markdown,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                cancellationToken);

            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }

        return new RunbookArtifact(
            applicationId,
            fileName,
            File.GetLastWriteTimeUtc(path));
    }

    public async Task<string?> ReadAsync(
        string applicationId,
        CancellationToken cancellationToken)
    {
        if (!Aegis.Configuration.Infrastructure.DirectoryContractStore.IsSafeApplicationId(applicationId))
            return null;

        string path = Path.Combine(_directory, applicationId + ".runbook.md");
        if (!File.Exists(path))
            return null;

        return await File.ReadAllTextAsync(path, cancellationToken);
    }

    public Task<IReadOnlyList<RunbookArtifact>> ListAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!Directory.Exists(_directory))
            return Task.FromResult<IReadOnlyList<RunbookArtifact>>([]);

        IReadOnlyList<RunbookArtifact> result = Directory
            .EnumerateFiles(_directory, "*.runbook.md", SearchOption.TopDirectoryOnly)
            .Select(path =>
            {
                string fileName = Path.GetFileName(path);
                string applicationId = fileName[..^".runbook.md".Length];
                return new RunbookArtifact(
                    applicationId,
                    fileName,
                    File.GetLastWriteTimeUtc(path));
            })
            .OrderBy(x => x.ApplicationId, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return Task.FromResult(result);
    }
}
