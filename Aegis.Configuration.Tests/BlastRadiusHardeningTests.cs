using Aegis.Configuration.Application;
using Aegis.Configuration.Domain;

namespace Aegis.Configuration.Tests;

public sealed class BlastRadiusHardeningTests
{
    [Fact]
    public void CriticalPath_Wins_WhenOptionalPathReachesSameIntermediateFirst()
    {
        DependencyGraph graph = new(
            [new("root", "Root", DependencyKind.CommonService), new("optional", "Optional", DependencyKind.CommonService), new("critical", "Critical", DependencyKind.CommonService), new("mid", "Mid", DependencyKind.CommonService), new("app", "App", DependencyKind.Application)],
            [new("optional", "root", false, "optional"), new("critical", "root", true, "critical"), new("mid", "optional", true, "optional path"), new("mid", "critical", true, "critical path"), new("app", "mid", true, "app")]);
        BlastRadiusReport report = new BlastRadiusAnalyzer().Analyze(graph, "root");
        Assert.True(report.Impacts.Single(x => x.ApplicationId == "app").Critical);
    }

    [Fact]
    public void Cycle_DoesNotLoopForever()
    {
        DependencyGraph graph = new([new("a", "A", DependencyKind.CommonService), new("b", "B", DependencyKind.CommonService), new("app", "App", DependencyKind.Application)], [new("b", "a", true, "b"), new("a", "b", true, "cycle"), new("app", "b", true, "app")]);
        BlastRadiusReport report = new BlastRadiusAnalyzer().Analyze(graph, "a");
        Assert.Single(report.Impacts);
    }
}
