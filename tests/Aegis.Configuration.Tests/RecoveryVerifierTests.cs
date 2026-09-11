using Aegis.Configuration.Application;

namespace Aegis.Configuration.Tests;

public sealed class RecoveryVerifierTests
{
    [Fact]
    public void Verify_ProvesFullRecovery()
    {
        OperationalRiskReport before = new(DateTimeOffset.UtcNow, [new("app", OperationalRiskLevel.Critical, 90, "Failure", "Fix", [])]);
        OperationalRiskReport after = new(DateTimeOffset.UtcNow, [new("app", OperationalRiskLevel.Healthy, 0, "OK", "None", [])]);
        RecoveryVerification result = new RecoveryVerifier().Verify(before, after);
        Assert.True(result.Recovered);
        Assert.Empty(result.RemainingApplications);
    }
}
