using Aegis.Configuration.Domain;
using Aegis.Configuration.Infrastructure;

namespace Aegis.Configuration.Tests;

public sealed class DirectoryContractStoreRegistrationTests
{
    [Fact]
    public async Task RegisterPersistsAndIsIdempotent()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"aegis-configuration-{Guid.NewGuid():N}");
        try
        {
            DirectoryContractStore store = new(directory);
            ApplicationConfigurationContract contract = CreateContract("Demo.App", "1.0.0", "Service:Url");

            var first = await store.RegisterAsync(contract);
            var second = await store.RegisterAsync(contract);
            IReadOnlyList<ApplicationConfigurationContract> loaded = await store.LoadAllAsync();

            Assert.True(first.Changed);
            Assert.False(second.Changed);
            Assert.Equal(first.ContractHash, second.ContractHash);
            Assert.Single(loaded);
            Assert.Equal("Demo.App", loaded[0].ApplicationId);
            Assert.Equal("1.0.0", loaded[0].Version);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task RegisterReplacesChangedContractForSameApplication()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"aegis-configuration-{Guid.NewGuid():N}");
        try
        {
            DirectoryContractStore store = new(directory);
            await store.RegisterAsync(CreateContract("Demo.App", "1.0.0", "Service:Url"));

            var changed = await store.RegisterAsync(CreateContract("Demo.App", "2.0.0", "Service:Endpoint"));
            IReadOnlyList<ApplicationConfigurationContract> loaded = await store.LoadAllAsync();

            Assert.True(changed.Changed);
            Assert.Single(loaded);
            Assert.Equal("2.0.0", loaded[0].Version);
            Assert.Equal("Service:Endpoint", loaded[0].Requirements.Single().ConfigurationKey);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task RegisterRejectsUnsafeApplicationId()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"aegis-configuration-{Guid.NewGuid():N}");
        try
        {
            DirectoryContractStore store = new(directory);
            ApplicationConfigurationContract contract = CreateContract("../Other.App", "1.0.0", "Service:Url");

            await Assert.ThrowsAsync<InvalidDataException>(async () => await store.RegisterAsync(contract));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    private static ApplicationConfigurationContract CreateContract(string applicationId, string version, string configurationKey)
        => new(
            applicationId,
            "Demo Application",
            version,
            [new ConfigurationRequirement("service-url", "Service URL", ConfigurationRequirementKind.Configuration, true, "Required endpoint.", configurationKey, Validator: "uri")]);
}
