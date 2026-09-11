using Aegis.Configuration.Application;
using Aegis.Configuration.Domain;

namespace Aegis.Configuration.Tests;

public sealed class DriftDetectorTests
{
    [Fact]
    public void Compare_FlagsVerifiedToMissingAsCritical()
    {
        CommissioningHistoryEntry previous = new(Guid.NewGuid(), DateTimeOffset.UtcNow, "app", "1", true, [new("db", "Database", ConfigurationState.Verified, "OK")]);
        CommissioningSnapshot current = new("app", "1", DateTimeOffset.UtcNow, new(false, [new("db", "Database", ConfigurationState.Missing, "Missing") ]));
        ConfigurationDriftReport report = new DriftDetector().Compare(previous, current);
        Assert.True(report.HasCriticalDrift);
        Assert.Equal(DriftSeverity.Critical, Assert.Single(report.Changes).Severity);
    }
}
