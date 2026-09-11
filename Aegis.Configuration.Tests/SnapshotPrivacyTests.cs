using Aegis.Configuration.Application;
using Aegis.Configuration.Domain;

namespace Aegis.Configuration.Tests;

public sealed class SnapshotPrivacyTests
{
    [Fact]
    public async Task ConnectionStyleConfiguration_IsNotFingerprintableByDefault()
    {
        IConfigurationValueResolver resolver = new StubResolver("Host=db;Username=user;Password=secret");
        ConfigurationRequirement requirement = new("db", "Database", ConfigurationRequirementKind.Configuration, true, "Database", "ConnectionStrings:Db");
        ApplicationConfigurationContract contract = new("app", "App", "1", [requirement]);
        ConfigurationEvaluation evaluation = new("app", "1", [new("db", "Database", true, ConfigurationState.Verified, "OK")]);
        SafeConfigurationSnapshot snapshot = await new SafeConfigurationSnapshotFactory(resolver).CaptureAsync(contract, evaluation);
        Assert.Null(snapshot.Requirements.Single().ValueFingerprint);
    }

    [Fact]
    public async Task ExplicitSafeOrdinaryValue_CanBeFingerprintCompared()
    {
        IConfigurationValueResolver resolver = new StubResolver("https://service.example");
        ConfigurationRequirement requirement = new("url", "URL", ConfigurationRequirementKind.Configuration, true, "Endpoint", "Service:Url", FingerprintSafe: true);
        ApplicationConfigurationContract contract = new("app", "App", "1", [requirement]);
        ConfigurationEvaluation evaluation = new("app", "1", [new("url", "URL", true, ConfigurationState.Verified, "OK")]);
        SafeConfigurationSnapshot snapshot = await new SafeConfigurationSnapshotFactory(resolver).CaptureAsync(contract, evaluation);
        Assert.NotNull(snapshot.Requirements.Single().ValueFingerprint);
    }

    private sealed class StubResolver(string value) : IConfigurationValueResolver
    {
        public ValueTask<string?> ResolveAsync(ConfigurationRequirement requirement, CancellationToken cancellationToken = default) => ValueTask.FromResult<string?>(value);
    }
}
