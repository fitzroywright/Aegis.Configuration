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
        Assert.Contains(result.Errors, x => x.Contains("Duplicate"));
        Assert.Contains(result.Errors, x => x.Contains("SecretName"));
    }
}
