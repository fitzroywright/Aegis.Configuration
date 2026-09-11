using Aegis.Configuration.Application;
using Aegis.Configuration.Domain;

namespace Aegis.Configuration.Tests;

public sealed class ContractValidatorTests
{
    [Fact]
    public void Validate_RejectsDuplicateIdsAndUnnamedSecret()
    {
        ApplicationConfigurationContract contract = new("demo", "Demo", "1.0", [
            new("same", "One", ConfigurationRequirementKind.Configuration, true, "Purpose", "A"),
            new("same", "Secret", ConfigurationRequirementKind.Secret, true, "Purpose")
        ]);

        ContractValidationResult result = new ContractValidator().Validate(contract);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, x => x.Contains("Duplicate", StringComparison.Ordinal));
        Assert.Contains(result.Errors, x => x.Contains("SecretName", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_RejectsMissingHumanReadableMetadata()
    {
        ApplicationConfigurationContract contract = new("demo", "", "1.0", [
            new("setting", "", ConfigurationRequirementKind.Configuration, true, "", "Setting")
        ]);

        ContractValidationResult result = new ContractValidator().Validate(contract);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, x => x.Contains("DisplayName", StringComparison.Ordinal));
        Assert.Contains(result.Errors, x => x.Contains("Purpose", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_RejectsUnknownValidator()
    {
        ApplicationConfigurationContract contract = new("demo", "Demo", "1.0", [
            new("setting", "Setting", ConfigurationRequirementKind.Configuration, true, "Purpose", "Setting", Validator: "typo-validator")
        ]);

        ContractValidationResult result = new ContractValidator().Validate(contract);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, x => x.Contains("unknown validator", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_RejectsSecretAndConfigurationLocatorMixing()
    {
        ApplicationConfigurationContract contract = new("demo", "Demo", "1.0", [
            new("secret", "Secret", ConfigurationRequirementKind.Secret, true, "Purpose", "Unsafe:ConfigurationKey", "safe/secret", "secret")
        ]);

        ContractValidationResult result = new ContractValidator().Validate(contract);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, x => x.Contains("cannot declare ConfigurationKey", StringComparison.Ordinal));
    }
}
