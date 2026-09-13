using Aegis.Configuration.Application;
using Aegis.Configuration.Domain;

namespace Aegis.Configuration.Tests;

public sealed class DriftDetectorTests
{
    [Fact]
    public void Compare_FlagsConfiguredToMissingAsCritical()
    {
        CommissioningHistoryEntry previous = new(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            "app",
            "1",
            true,
            [new("db", "Database", true, ConfigurationState.Configured, "Configured")]);

        CommissioningSnapshot current = new(
            "app",
            "1",
            DateTimeOffset.UtcNow,
            new("app", "1", [new("db", "Database", true, ConfigurationState.Missing, "Missing")]));

        ConfigurationDriftReport report = new DriftDetector().Compare(previous, current);

        Assert.True(report.HasCriticalDrift);
        Assert.Equal(DriftSeverity.Critical, Assert.Single(report.Changes).Severity);
    }
}
