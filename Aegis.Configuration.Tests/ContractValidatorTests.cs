using Aegis.Configuration.Application;
using Aegis.Configuration.Domain;

namespace Aegis.Configuration.Tests;

public sealed class ContractValidatorTests
{
    [Fact]
    public void Validate_RejectsDuplicateIds()
    {
        ApplicationConfigurationContract contract = new("demo", "Demo", "1.0", [
            new("same", "One", ConfigurationRequirementKind.Configuration, true, "Purpose", IsConfigured: true),
            new("same", "Secret", ConfigurationRequirementKind.Secret, true, "Purpose", IsConfigured: true, Sensitive: true)
        ]);

        ContractValidationResult result = new ContractValidator().Validate(contract);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, x => x.Contains("Duplicate", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_RejectsMissingHumanReadableMetadata()
    {
        ApplicationConfigurationContract contract = new("demo", "", "1.0", [
            new("setting", "", ConfigurationRequirementKind.Configuration, true, "", IsConfigured: false)
        ]);

        ContractValidationResult result = new ContractValidator().Validate(contract);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, x => x.Contains("DisplayName", StringComparison.Ordinal));
        Assert.Contains(result.Errors, x => x.Contains("Purpose", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_AllowsSelfReportedSecretWithoutSecretValueOrLocator()
    {
        ApplicationConfigurationContract contract = new("demo", "Demo", "1.0", [
            new("secret", "OpenBao credentials", ConfigurationRequirementKind.Secret, true, "Credentials required to reach OpenBao.", IsConfigured: true, Sensitive: true)
        ]);

        ContractValidationResult result = new ContractValidator().Validate(contract);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_RejectsSensitiveDisplayValue()
    {
        ApplicationConfigurationContract contract = new("demo", "Demo", "1.0", [
            new("secret", "Secret", ConfigurationRequirementKind.Secret, true, "Purpose", IsConfigured: true, SafeDisplayValue: "do-not-publish", Sensitive: true)
        ]);

        ContractValidationResult result = new ContractValidator().Validate(contract);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, x => x.Contains("cannot publish a display value", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_RejectsSecretAndConfigurationLocatorMixing()
    {
        ApplicationConfigurationContract contract = new("demo", "Demo", "1.0", [
            new("secret", "Secret", ConfigurationRequirementKind.Secret, true, "Purpose", ConfigurationKey: "Unsafe:ConfigurationKey", IsConfigured: true, Sensitive: true)
        ]);

        ContractValidationResult result = new ContractValidator().Validate(contract);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, x => x.Contains("cannot declare ConfigurationKey", StringComparison.Ordinal));
    }
}
