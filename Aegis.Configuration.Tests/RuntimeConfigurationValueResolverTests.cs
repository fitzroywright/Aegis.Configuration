using Aegis.Configuration.Domain;
using Aegis.Configuration.Infrastructure;
using Microsoft.Extensions.Configuration;

namespace Aegis.Configuration.Tests;

public sealed class RuntimeConfigurationValueResolverTests
{
    [Fact]
    public async Task ResolveAsync_UsesConfigurationForNormalRequirement()
    {
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Demo:Value"] = "abc" }).Build();
        RuntimeConfigurationValueResolver resolver = new(configuration, new NullSecretValueResolver());
        ConfigurationRequirement requirement = new("demo", "Demo", ConfigurationRequirementKind.Configuration, true, "Purpose", "Demo:Value");

        string? value = await resolver.ResolveAsync(requirement);

        Assert.Equal("abc", value);
    }
}
