using Aegis.Configuration.Domain;
using Aegis.Configuration.Infrastructure;

namespace Aegis.Configuration.Tests;

public sealed class SecretResolvedRequirementValidatorTests
{
    [Fact]
    public async Task ValidateAsync_ProvesResolutionWithoutReturningValue()
    {
        ConfigurationRequirement requirement = new("secret", "Secret", ConfigurationRequirementKind.Secret, true, "Purpose", SecretName: "App:Secret", Validator: "secret");
        SecretResolvedRequirementValidator validator = new();
        (bool success, string message) = await validator.ValidateAsync(requirement, "extremely-sensitive-value");
        Assert.True(success);
        Assert.DoesNotContain("extremely-sensitive-value", message);
    }
}
