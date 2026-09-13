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
        ConfigurationEvaluator evaluator = new(new EmptyResolver());

        ConfigurationEvaluation result = await evaluator.EvaluateAsync(contract);

        Assert.False(result.IsComplete);
        Assert.Equal(ConfigurationState.Missing, result.Requirements.Single().State);
    }

    [Fact]
    public async Task MissingOptionalValueDoesNotBlockCompleteness()
    {
        ApplicationConfigurationContract contract = new("app", "App", "1.0", [
            new("url", "Optional URL", ConfigurationRequirementKind.Configuration, false, "Optional integration", "Service:Url")
        ]);
        ConfigurationEvaluator evaluator = new(new EmptyResolver());

        ConfigurationEvaluation result = await evaluator.EvaluateAsync(contract);

        Assert.True(result.IsComplete);
        Assert.Equal(ConfigurationState.OptionalNotConfigured, result.Requirements.Single().State);
    }

    [Fact]
    public async Task PresentRequiredValueIsConfigurationCompleteWithoutRuntimeProbe()
    {
        ApplicationConfigurationContract contract = new("app", "App", "1.0", [
            new("url", "Service URL", ConfigurationRequirementKind.Configuration, true, "Required service", "Service:Url")
        ]);
        ConfigurationEvaluator evaluator = new(new FixedResolver("https://example.invalid"));

        ConfigurationEvaluation result = await evaluator.EvaluateAsync(contract);

        Assert.True(result.IsComplete);
        Assert.Equal(ConfigurationState.Configured, result.Requirements.Single().State);
    }

    [Fact]
    public async Task ApplicationMaySelfReportConfiguredWithoutPublishingValue()
    {
        ApplicationConfigurationContract contract = new("app", "App", "1.0", [
            new("secret", "OpenBao Credentials", ConfigurationRequirementKind.Secret, true, "OpenBao credentials are supplied.", SecretName: "openbao/bootstrap", IsConfigured: true, Sensitive: true)
        ]);
        ConfigurationEvaluator evaluator = new(new EmptyResolver());

        ConfigurationEvaluation result = await evaluator.EvaluateAsync(contract);

        Assert.True(result.IsComplete);
        RequirementEvaluation requirement = result.Requirements.Single();
        Assert.Equal(ConfigurationState.Configured, requirement.State);
        Assert.Null(requirement.SafeDisplayValue);
        Assert.True(requirement.Sensitive);
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
