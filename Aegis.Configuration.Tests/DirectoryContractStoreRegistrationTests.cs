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
