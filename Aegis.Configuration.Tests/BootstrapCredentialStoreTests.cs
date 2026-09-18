using Aegis.Configuration.Infrastructure;

namespace Aegis.Configuration.Tests;

public sealed class BootstrapCredentialStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "aegis-config-bootstrap-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Created_key_validates_and_plaintext_is_not_persisted()
    {
        string path = Path.Combine(_root, "credentials.json");
        var store = new BootstrapCredentialStore(path);

        BootstrapCredentialCreated created = await store.CreateAsync(
            new BootstrapCredentialCreateRequest("Operations test", 30));

        Assert.StartsWith("aegis_cfg_", created.Key);
        Assert.True(await store.ValidateAsync(created.Key));

        string persisted = await File.ReadAllTextAsync(path);
        Assert.DoesNotContain(created.Key, persisted);
        Assert.Contains(created.Id, persisted);
    }

    [Fact]
    public async Task Revoked_key_no_longer_validates()
    {
        string path = Path.Combine(_root, "credentials.json");
        var store = new BootstrapCredentialStore(path);
        BootstrapCredentialCreated created = await store.CreateAsync(
            new BootstrapCredentialCreateRequest(null, 30));

        Assert.True(await store.RevokeAsync(created.Id));
        Assert.False(await store.ValidateAsync(created.Key));
    }

    [Fact]
    public async Task Unknown_key_is_rejected()
    {
        string path = Path.Combine(_root, "credentials.json");
        var store = new BootstrapCredentialStore(path);
        await store.CreateAsync(new BootstrapCredentialCreateRequest(null, 30));

        Assert.False(await store.ValidateAsync("aegis_cfg_not-a-real-key"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, true);
    }
}
