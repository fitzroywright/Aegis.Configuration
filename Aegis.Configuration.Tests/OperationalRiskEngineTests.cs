using Aegis.Configuration.Application;
using Aegis.Configuration.Domain;

namespace Aegis.Configuration.Tests;

public sealed class OperationalRiskEngineTests
{
    [Fact]
    public void Analyze_PrioritizesCriticalDependencyFailure()
    {
        CommissioningSnapshot snapshot = new("app", "1", DateTimeOffset.UtcNow, new("app", "1", [new("db", "Database", true, ConfigurationState.Verified, "OK")]));
        BlastRadiusReport blast = new("OpenBao", DateTimeOffset.UtcNow, [new("app", "OpenBao", true, 2, ["OpenBao", "Common.Secrets", "app"], "Credentials")]);
        OperationalRiskReport report = new OperationalRiskEngine().Analyze([snapshot], new Dictionary<string, ConfigurationDriftReport>(), [blast]);
        OperationalRiskItem item = Assert.Single(report.Items);
        Assert.Equal(OperationalRiskLevel.Critical, item.Level);
        Assert.True(item.Priority >= 40);
    }
}
