using Aegis.Configuration.Application;
using Aegis.Configuration.Domain;

namespace Aegis.Configuration.Tests;

public sealed class PresentationContractValidationTests
{
    [Fact]
    public void Https_icon_is_allowed()
    {
        ApplicationConfigurationContract contract = Contract(
            new ApplicationPresentationMetadata("https://example.org/icon.svg", "Example", "gold"));

        ContractValidationResult result = new ContractValidator().Validate(contract);

        Assert.DoesNotContain(result.Errors, x => x.Contains("Presentation.IconUrl", StringComparison.Ordinal));
    }

    [Fact]
    public void Unsafe_icon_scheme_is_rejected()
    {
        ApplicationConfigurationContract contract = Contract(
            new ApplicationPresentationMetadata("javascript:alert(1)", "Example", "gold"));

        ContractValidationResult result = new ContractValidator().Validate(contract);

        Assert.Contains(result.Errors, x => x.Contains("Presentation.IconUrl", StringComparison.Ordinal));
    }

    private static ApplicationConfigurationContract Contract(ApplicationPresentationMetadata presentation) =>
        new(
            "Aegis.Example",
            "Aegis Example",
            "1.0.0",
            [
                new ConfigurationRequirement(
                    "public-url",
                    "Public URL",
                    ConfigurationRequirementKind.Configuration,
                    true,
                    "Example requirement.",
                    ConfigurationKey: "Aegis:PublicUrl",
                    IsConfigured: true)
            ],
            Presentation: presentation);
}
