using Aegis.Configuration.Application;
using Aegis.Configuration.Domain;

namespace Aegis.Configuration.Tests;

public sealed class ConfigurationEvaluatorTests
{
    [Fact]
    public async Task MissingRequiredValueIsReportedMissing()
    {
        ApplicationConfigurationContract contract = new("app", "App", "1.0", [
            new("db", "Database", ConfigurationRequirementKind.Configuration, true, "Required database", "ConnectionStrings:App")
        ]);
        ConfigurationEvaluator evaluator = new(new EmptyResolver(), []);

        ConfigurationEvaluation result = await evaluator.EvaluateAsync(contract);

        Assert.False(result.IsOperational);
        Assert.Equal(ConfigurationState.Missing, result.Requirements.Single().State);
    }

    [Fact]
    public async Task PresentValueWithoutValidatorIsUnverified()
    {
        ApplicationConfigurationContract contract = new("app", "App", "1.0", [
            new("url", "Service URL", ConfigurationRequirementKind.Configuration, true, "Required service", "Service:Url")
        ]);
        ConfigurationEvaluator evaluator = new(new FixedResolver("https://example.invalid"), []);

        ConfigurationEvaluation result = await evaluator.EvaluateAsync(contract);

        Assert.False(result.IsOperational);
        Assert.Equal(ConfigurationState.ConfiguredUnverified, result.Requirements.Single().State);
    }

    private sealed class EmptyResolver : IConfigurationValueResolver
    {
        public ValueTask<string?> ResolveAsync(ConfigurationRequirement requirement, CancellationToken cancellationToken = default) => ValueTask.FromResult<string?>(null);
    }

    private sealed class FixedResolver(string value) : IConfigurationValueResolver
    {
        public ValueTask<string?> ResolveAsync(ConfigurationRequirement requirement, CancellationToken cancellationToken = default) => ValueTask.FromResult<string?>(value);
    }
}
