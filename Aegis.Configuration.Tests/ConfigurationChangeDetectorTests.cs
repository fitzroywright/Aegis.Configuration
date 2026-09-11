using Aegis.Configuration.Application;
using Aegis.Configuration.Domain;

namespace Aegis.Configuration.Tests;

public sealed class ConfigurationChangeDetectorTests
{
    [Fact]
    public void Compare_DetectsFingerprintChangeWithoutValues()
    {
        SafeConfigurationSnapshot before = new("app", "1", DateTimeOffset.UtcNow, [new("url", ConfigurationRequirementKind.Configuration, ConfigurationState.Verified, true, "AAA")]);
        SafeConfigurationSnapshot after = new("app", "1", DateTimeOffset.UtcNow, [new("url", ConfigurationRequirementKind.Configuration, ConfigurationState.Verified, true, "BBB")]);
        SafeSnapshotDiff diff = new ConfigurationChangeDetector().Compare(before, after);
        ConfigurationValueChange change = Assert.Single(diff.Changes);
        Assert.True(change.FingerprintChanged);
    }
}
