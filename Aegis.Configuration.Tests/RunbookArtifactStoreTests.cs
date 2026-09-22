namespace Aegis.Configuration.Tests;

public sealed class RunbookArtifactStoreTests
{
    [Fact]
    public async Task Generated_edit_and_restore_are_versioned_without_destroying_history()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"aegis-runbooks-{Guid.NewGuid():N}");
        try
        {
            CancellationToken ct = TestContext.Current.CancellationToken;
            RunbookArtifactStore store = new(directory);

            RunbookArtifact generated = await store.SaveAsync("Demo.App", "# Generated\n", ct);
            RunbookArtifact edited = await store.SaveEditAsync("Demo.App", "# Operator edit\n", "Test User", "Clarified recovery.", ct);
            RunbookArtifact? restored = await store.RestoreAsync("Demo.App", 1, "Test User", ct);

            Assert.Equal(1, generated.CurrentVersion);
            Assert.Equal(2, edited.CurrentVersion);
            Assert.NotNull(restored);
            Assert.Equal(3, restored.CurrentVersion);
            Assert.Equal("# Generated\n", await store.ReadAsync("Demo.App", ct));

            IReadOnlyList<RunbookVersion> versions = await store.ListVersionsAsync("Demo.App", ct);
            Assert.Equal([3, 2, 1], versions.Select(x => x.Version).ToArray());
            Assert.Equal("Restored", versions[0].Source);
            Assert.Equal("Test User", versions[0].Author);
            Assert.Equal("Edited", versions[1].Source);
            Assert.Equal("Clarified recovery.", versions[1].Note);
            Assert.Equal("Generated", versions[2].Source);
            Assert.Equal("# Operator edit\n", await store.ReadVersionAsync("Demo.App", 2, ct));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task Saving_identical_content_does_not_create_noise_version()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"aegis-runbooks-{Guid.NewGuid():N}");
        try
        {
            CancellationToken ct = TestContext.Current.CancellationToken;
            RunbookArtifactStore store = new(directory);

            RunbookArtifact first = await store.SaveAsync("Demo.App", "# Same\n", ct);
            RunbookArtifact second = await store.SaveEditAsync("Demo.App", "# Same\n", "Test User", "No content change.", ct);

            Assert.Equal(1, first.CurrentVersion);
            Assert.Equal(1, second.CurrentVersion);
            Assert.Single(await store.ListVersionsAsync("Demo.App", ct));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
}
