using Aegis.Configuration.Application;
using Aegis.Configuration.Domain;

namespace Aegis.Configuration.Tests;

public sealed class BlastRadiusAnalyzerTests
{
    [Fact]
    public void Analyze_FollowsTransitiveDependencyToApplications()
    {
        DependencyGraph graph = new([
            new("vault", "Vault", DependencyKind.SecretProvider),
            new("secrets", "Secrets", DependencyKind.CommonService),
            new("app", "App", DependencyKind.Application)
        ], [
            new("secrets", "vault", true, "Secret backend"),
            new("app", "secrets", true, "Credentials")
        ]);
        BlastRadiusReport report = new BlastRadiusAnalyzer().Analyze(graph, "vault");
        DependencyImpact impact = Assert.Single(report.Impacts);
        Assert.Equal("app", impact.ApplicationId);
        Assert.True(impact.Critical);
        Assert.Equal(2, impact.Distance);
    }
}
