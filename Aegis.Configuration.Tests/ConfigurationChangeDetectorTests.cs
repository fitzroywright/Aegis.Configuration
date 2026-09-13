using Aegis.Configuration.Application;
using Aegis.Configuration.Domain;

namespace Aegis.Configuration.Tests;

public sealed class ConfigurationChangeDetectorTests
{
    [Fact]
    public void Compare_DetectsConfiguredPresenceChange()
    {
        SafeConfigurationSnapshot before = new("app", "1", DateTimeOffset.UtcNow, [new("url", ConfigurationRequirementKind.Configuration, ConfigurationState.Missing, false)]);
        SafeConfigurationSnapshot after = new("app", "1", DateTimeOffset.UtcNow, [new("url", ConfigurationRequirementKind.Configuration, ConfigurationState.Configured, true)]);
        SafeSnapshotDiff diff = new ConfigurationChangeDetector().Compare(before, after);
        ConfigurationValueChange change = Assert.Single(diff.Changes);
        Assert.True(change.PresenceChanged);
        Assert.Contains("now reported as configured", change.Description, StringComparison.Ordinal);
    }
}
