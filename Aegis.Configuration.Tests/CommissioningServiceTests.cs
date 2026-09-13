using Aegis.Configuration.Application;
using Aegis.Configuration.Domain;

namespace Aegis.Configuration.Tests;

public sealed class CommissioningServiceTests
{
    [Fact]
    public async Task EvaluateAllAsync_ReturnsApplicationReportedState()
    {
        ApplicationConfigurationContract contract = new("app", "App", "1.0", [
            new("setting", "Setting", ConfigurationRequirementKind.Configuration, true, "Purpose", ConfigurationKey: "Setting", IsConfigured: true)
        ]);
        CommissioningService service = new(new FakeStore(contract), new ConfigurationEvaluator());

        IReadOnlyList<CommissioningSnapshot> result = await service.EvaluateAllAsync();

        Assert.Single(result);
        Assert.True(result[0].Evaluation.IsComplete);
        Assert.Equal(ConfigurationState.Configured, result[0].Evaluation.Requirements[0].State);
    }

    private sealed class FakeStore(ApplicationConfigurationContract contract) : IConfigurationContractStore
    {
        public ValueTask<IReadOnlyList<ApplicationConfigurationContract>> LoadAllAsync(CancellationToken cancellationToken = default)
            => ValueTask.FromResult<IReadOnlyList<ApplicationConfigurationContract>>([contract]);
    }
}
