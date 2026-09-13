using Aegis.Configuration.Application;
using Aegis.Configuration.Domain;

namespace Aegis.Configuration.Tests;

public sealed class SafeConfigurationSnapshotTests
{
    [Fact]
    public async Task CaptureAsync_NeverFingerprintsSecretValues()
    {
        ApplicationConfigurationContract contract = new("app", "App", "1", [new("secret", "Secret", ConfigurationRequirementKind.Secret, true, "Purpose", SecretName: "secret", Sensitive: true)]);
        ConfigurationEvaluation evaluation = new("app", "1", [new("secret", "Secret", true, ConfigurationState.Configured, "Configured", Sensitive: true)]);
        SafeConfigurationSnapshotFactory factory = new(new Resolver("do-not-store-me"));
        SafeConfigurationSnapshot snapshot = await factory.CaptureAsync(contract, evaluation);
        SafeRequirementSnapshot result = Assert.Single(snapshot.Requirements);
        Assert.True(result.ValuePresent);
        Assert.Null(result.ValueFingerprint);
    }

    private sealed class Resolver(string value) : IConfigurationValueResolver
    {
        public ValueTask<string?> ResolveAsync(ConfigurationRequirement requirement, CancellationToken cancellationToken = default) => ValueTask.FromResult<string?>(value);
    }
}
