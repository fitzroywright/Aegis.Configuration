using Aegis.Configuration.Application;
using Aegis.Configuration.Domain;

namespace Aegis.Configuration.Tests;

public sealed class BlastRadiusAnalyzerTests
{
    [Fact]
    public void Analyze_ReturnsEveryAffectedApplication()
    {
        ApplicationDependencyMap[] maps = [
            new("A", [new("OpenBao", "OpenBao", DependencyKind.SecretProvider, true)]),
            new("B", [new("OpenBao", "OpenBao", DependencyKind.SecretProvider, false)]),
            new("C", [new("Other", "Other", DependencyKind.ExternalService, true)])
        ];
        BlastRadiusReport result = new BlastRadiusAnalyzer().Analyze("OpenBao", maps);
        Assert.Equal(2, result.Impacts.Count);
        Assert.Equal("A", result.Impacts[0].ApplicationId);
    }
}
