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
    public async Task OptionalDisabledCapabilityIsExplicitlyDisabledAndDoesNotBlockCompleteness()
    {
        ApplicationConfigurationContract contract = new("app", "App", "1.0", [
            new("teams", "Teams", ConfigurationRequirementKind.ExternalService, false, "Optional Teams delivery",
                IsConfigured: false,
                ConfigurationState: "Disabled",
                VerificationStatus: "NOT REQUIRED")
        ]);

        ConfigurationEvaluation result = await new ConfigurationEvaluator().EvaluateAsync(contract, TestContext.Current.CancellationToken);

        Assert.True(result.IsComplete);
        RequirementEvaluation requirement = result.Requirements.Single();
        Assert.Equal(ConfigurationState.Disabled, requirement.State);
        Assert.Equal("NOT REQUIRED", requirement.VerificationStatus);
    }

    [Fact]
    public async Task RequiredCapabilityCannotHideBehindDisabledState()
    {
        ApplicationConfigurationContract contract = new("app", "App", "1.0", [
            new("oidc", "OIDC", ConfigurationRequirementKind.ExternalService, true, "Required authentication",
                IsConfigured: false,
                ConfigurationState: "Disabled")
        ]);

        ConfigurationEvaluation result = await new ConfigurationEvaluator().EvaluateAsync(contract, TestContext.Current.CancellationToken);

        Assert.False(result.IsComplete);
        Assert.Equal(ConfigurationState.Missing, result.Requirements.Single().State);
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
    public async Task RequiredSettingSatisfiedByCodeDefaultIsCompleteButNotExplicitlyConfigured()
    {
        ApplicationConfigurationContract contract = new("app", "App", "1.0", [
            new("port", "Directory Port", ConfigurationRequirementKind.Configuration, true, "LDAP port",
                ConfigurationKey: "Security:Port",
                IsConfigured: false,
                SafeDisplayValue: "636",
                ConfigurationState: "Default",
                EffectiveValueAvailable: true,
                EffectiveSource: "Runtime/Code Default",
                HasDefault: true,
                DefaultValue: "636")
        ]);

        ConfigurationEvaluation result = await new ConfigurationEvaluator().EvaluateAsync(contract, TestContext.Current.CancellationToken);

        Assert.True(result.IsComplete);
        RequirementEvaluation requirement = result.Requirements.Single();
        Assert.Equal(ConfigurationState.Default, requirement.State);
        Assert.False(requirement.ExplicitlyConfigured);
        Assert.True(requirement.EffectiveValueAvailable);
        Assert.True(requirement.HasDefault);
        Assert.Equal("636", requirement.DefaultValue);
        Assert.Equal("Runtime/Code Default", requirement.EffectiveSource);
    }

    [Fact]
    public async Task UnknownRequiredSecretDoesNotPretendToBeComplete()
    {
        ApplicationConfigurationContract contract = new("app", "App", "1.0", [
            new("secret", "Bootstrap", ConfigurationRequirementKind.Secret, true, "Bootstrap secret",
                IsConfigured: null,
                Sensitive: true,
                ConfigurationState: "Unknown",
                EffectiveValueAvailable: null,
                EffectiveSource: "Common.Secrets bootstrap",
                VerificationStatus: "NotDirectlyVerifiable")
        ]);

        ConfigurationEvaluation result = await new ConfigurationEvaluator().EvaluateAsync(contract, TestContext.Current.CancellationToken);

        Assert.False(result.IsComplete);
        Assert.Equal(ConfigurationState.Unknown, result.Requirements.Single().State);
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
