using Aegis.Configuration.Application;
using Aegis.Configuration.Domain;

namespace Aegis.Configuration.Tests;

public sealed class CommissioningServiceTests
{
    [Fact]
    public async Task EvaluateAllAsync_ReturnsSnapshotForEachContract()
    {
        ApplicationConfigurationContract contract = new("app", "App", "1.0", [
            new("setting", "Setting", ConfigurationRequirementKind.Configuration, true, "Purpose", "Setting")
        ]);
        CommissioningService service = new(new FakeStore(contract), new ConfigurationEvaluator(new FakeResolver("present"), []));

        IReadOnlyList<CommissioningSnapshot> result = await service.EvaluateAllAsync();

        Assert.Single(result);
        Assert.Equal(ConfigurationState.ConfiguredUnverified, result[0].Evaluation.Requirements[0].State);
    }

    private sealed class FakeStore(ApplicationConfigurationContract contract) : IConfigurationContractStore
    {
        public ValueTask<IReadOnlyList<ApplicationConfigurationContract>> LoadAllAsync(CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult<IReadOnlyList<ApplicationConfigurationContract>>([contract]);
        }
    }

    private sealed class FakeResolver(string value) : IConfigurationValueResolver
    {
        public ValueTask<string?> ResolveAsync(ConfigurationRequirement requirement, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult<string?>(value);
        }
    }
}
