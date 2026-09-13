using Aegis.Configuration.Application;
using Aegis.Configuration.Domain;

namespace Aegis.Configuration.Tests;

public sealed class ConfigurationReadinessScorerTests
{
    [Fact]
    public void Calculate_GradesMixedConfiguration()
    {
        ConfigurationEvaluation evaluation = new(
            "app",
            "1",
            [
                new("a", "A", true, ConfigurationState.Configured, "Configured"),
                new("b", "B", true, ConfigurationState.Missing, "Missing")
            ]);

        ConfigurationReadinessScore score = new ConfigurationReadinessScorer().Calculate(evaluation);

        Assert.Equal(50, score.Score);
        Assert.Equal(1, score.Configured);
        Assert.Equal("Orange", score.Grade);
    }
}
