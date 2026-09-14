using Aegis.Configuration.Application;
using Aegis.Configuration.Domain;

namespace Aegis.Configuration.Tests;

public sealed class ConfigurationEvaluatorTests
{
    [Fact]
    public async Task RequiredSettingReportedMissingIsIncomplete()
    {
        ApplicationConfigurationContract contract = new("app", "App", "1.0", [
            new("db", "Database", ConfigurationRequirementKind.Configuration, true, "Required database", ConfigurationKey: "ConnectionStrings:App", IsConfigured: false)
        ]);

        ConfigurationEvaluation result = await new ConfigurationEvaluator().EvaluateAsync(contract, TestContext.Current.CancellationToken);

        Assert.False(result.IsComplete);
        Assert.Equal(ConfigurationState.Missing, result.Requirements.Single().State);
    }

    [Fact]
    public async Task OptionalSettingReportedMissingDoesNotBlockCompleteness()
    {
        ApplicationConfigurationContract contract = new("app", "App", "1.0", [
            new("url", "Optional URL", ConfigurationRequirementKind.Configuration, false, "Optional integration", ConfigurationKey: "Service:Url", IsConfigured: false)
        ]);

        ConfigurationEvaluation result = await new ConfigurationEvaluator().EvaluateAsync(contract, TestContext.Current.CancellationToken);

        Assert.True(result.IsComplete);
        Assert.Equal(ConfigurationState.OptionalNotConfigured, result.Requirements.Single().State);
    }

    [Fact]
    public async Task ApplicationReportedConfiguredIsCompleteWithoutRuntimeProbe()
    {
        ApplicationConfigurationContract contract = new("app", "App", "1.0", [
            new("url", "Service URL", ConfigurationRequirementKind.ExternalService, true, "Required service", ConfigurationKey: "Service:Url", IsConfigured: true, SafeDisplayValue: "https://example.invalid")
        ]);

        ConfigurationEvaluation result = await new ConfigurationEvaluator().EvaluateAsync(contract, TestContext.Current.CancellationToken);

        Assert.True(result.IsComplete);
        RequirementEvaluation requirement = result.Requirements.Single();
        Assert.Equal(ConfigurationState.Configured, requirement.State);
        Assert.Equal("https://example.invalid", requirement.SafeDisplayValue);
    }

    [Fact]
    public async Task SensitiveValueIsNeverExposed()
    {
        ApplicationConfigurationContract contract = new("app", "App", "1.0", [
            new("secret", "OpenBao Credentials", ConfigurationRequirementKind.Secret, true, "OpenBao credentials are supplied.", SecretName: "openbao/bootstrap", IsConfigured: true, Sensitive: true)
        ]);

        ConfigurationEvaluation result = await new ConfigurationEvaluator().EvaluateAsync(contract, TestContext.Current.CancellationToken);

        Assert.True(result.IsComplete);
        RequirementEvaluation requirement = result.Requirements.Single();
        Assert.Null(requirement.SafeDisplayValue);
        Assert.True(requirement.Sensitive);
    }
}
