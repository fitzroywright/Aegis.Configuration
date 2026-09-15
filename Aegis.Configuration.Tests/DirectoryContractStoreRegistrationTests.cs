using Aegis.Configuration.Domain;
using Aegis.Configuration.Infrastructure;

namespace Aegis.Configuration.Tests;

public sealed class DirectoryContractStoreRegistrationTests
{
    [Fact]
    public async Task RegisterPersistsAndRefreshesTimestampWithoutChangingSemanticHash()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"aegis-configuration-{Guid.NewGuid():N}");
        try
        {
            CancellationToken cancellationToken = TestContext.Current.CancellationToken;
            DirectoryContractStore store = new(directory);
            ApplicationConfigurationContract contract = CreateContract("Demo.App", "1.0.0", "Service:Url", true);
            var first = await store.RegisterAsync(contract, cancellationToken);
            await Task.Delay(5, cancellationToken);
            var second = await store.RegisterAsync(contract, cancellationToken);
            IReadOnlyList<ApplicationConfigurationContract> loaded = await store.LoadAllAsync(cancellationToken);
            Assert.True(first.Changed);
            Assert.False(second.Changed);
            Assert.Equal(first.ContractHash, second.ContractHash);
            Assert.True(second.LastRegisteredAtUtc > first.LastRegisteredAtUtc);
            Assert.Equal(second.LastRegisteredAtUtc, loaded.Single().LastRegisteredAtUtc);
            Assert.True(loaded[0].Requirements.Single().IsConfigured);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task RuntimeIdentityIsPersistedButDoesNotChangeSemanticHash()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"aegis-configuration-{Guid.NewGuid():N}");
        try
        {
            CancellationToken cancellationToken = TestContext.Current.CancellationToken;
            DirectoryContractStore store = new(directory);
            ApplicationConfigurationContract firstContract = CreateContract("Demo.App", "1.0.0", "Service:Url", true) with
            {
                RuntimeEnvironment = new RuntimeEnvironmentSnapshot(
                    MachineName: "host-a",
                    HostName: "host-a.fftpj.local",
                    HostingEnvironment: "Production",
                    OperatingSystem: "Test OS",
                    ProcessorCount: 4,
                    Storage: [new RuntimeStorageVolume("/", "Fixed", "testfs")])
            };
            ApplicationConfigurationContract secondContract = firstContract with
            {
                RuntimeEnvironment = firstContract.RuntimeEnvironment! with
                {
                    MachineName = "host-b",
                    HostName = "host-b.fftpj.local",
                    Storage = [new RuntimeStorageVolume("D:", "Fixed", "testfs")]
                }
            };

            var first = await store.RegisterAsync(firstContract, cancellationToken);
            var second = await store.RegisterAsync(secondContract, cancellationToken);
            ApplicationConfigurationContract loaded = Assert.Single(await store.LoadAllAsync(cancellationToken));

            Assert.True(first.Changed);
            Assert.False(second.Changed);
            Assert.Equal(first.ContractHash, second.ContractHash);
            Assert.Equal("host-b", loaded.RuntimeEnvironment?.MachineName);
            Assert.Equal("host-b.fftpj.local", loaded.RuntimeEnvironment?.HostName);
            Assert.Equal("D:", loaded.RuntimeEnvironment?.Storage?.Single().Name);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task ClientCannotForgeRegistrationTimestamp()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"aegis-configuration-{Guid.NewGuid():N}");
        try
        {
            DirectoryContractStore store = new(directory);
            DateTimeOffset forged = DateTimeOffset.UtcNow.AddYears(10);
            ApplicationConfigurationContract contract = CreateContract("Demo.App", "1.0.0", "Service:Url", true) with { LastRegisteredAtUtc = forged };
            var result = await store.RegisterAsync(contract, TestContext.Current.CancellationToken);
            Assert.NotEqual(forged, result.LastRegisteredAtUtc);
            Assert.True(result.LastRegisteredAtUtc < forged);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task RegisterReplacesChangedReportedState()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"aegis-configuration-{Guid.NewGuid():N}");
        try
        {
            CancellationToken cancellationToken = TestContext.Current.CancellationToken;
            DirectoryContractStore store = new(directory);
            await store.RegisterAsync(CreateContract("Demo.App", "1.0.0", "Service:Url", false), cancellationToken);
            var changed = await store.RegisterAsync(CreateContract("Demo.App", "1.0.0", "Service:Url", true), cancellationToken);
            IReadOnlyList<ApplicationConfigurationContract> loaded = await store.LoadAllAsync(cancellationToken);
            Assert.True(changed.Changed);
            Assert.True(loaded.Single().Requirements.Single().IsConfigured);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task RetiredCafeteriaPublisherIsAcceptedButNeverPersistedOrDisplayed()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"aegis-configuration-{Guid.NewGuid():N}");
        try
        {
            CancellationToken cancellationToken = TestContext.Current.CancellationToken;
            DirectoryContractStore store = new(directory);
            ApplicationConfigurationContract legacy = CreateContract("Cafeteria.Services", "1.0.0", "Service:Url", true);

            var result = await store.RegisterAsync(legacy, cancellationToken);
            IReadOnlyList<ApplicationConfigurationContract> loaded = await store.LoadAllAsync(cancellationToken);

            Assert.False(result.Changed);
            Assert.Empty(loaded);
            Assert.False(File.Exists(Path.Combine(directory, "Cafeteria.Services.configuration-contract.json")));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task RegisterRejectsUnsafeApplicationId()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"aegis-configuration-{Guid.NewGuid():N}");
        try
        {
            DirectoryContractStore store = new(directory);
            await Assert.ThrowsAsync<InvalidDataException>(async () =>
                await store.RegisterAsync(CreateContract("../Other.App", "1.0.0", "Service:Url", true), TestContext.Current.CancellationToken));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    private static ApplicationConfigurationContract CreateContract(string applicationId, string version, string configurationKey, bool configured)
        => new(applicationId, "Demo Application", version, [new ConfigurationRequirement("service-url", "Service URL", ConfigurationRequirementKind.ExternalService, true, "Required endpoint.", ConfigurationKey: configurationKey, IsConfigured: configured, SafeDisplayValue: configured ? "https://example.invalid" : null)]);
}
