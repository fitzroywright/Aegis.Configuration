using Aegis.Configuration.Application;
using Aegis.Configuration.Domain;

namespace Aegis.Configuration.Tests;

public sealed class ConfigurationReadinessScorerTests
{
    [Fact]
    public void Calculate_GradesMixedConfiguration()
    {
        ConfigurationEvaluation evaluation = new(false, [new("a", "A", ConfigurationState.Verified, "OK"), new("b", "B", ConfigurationState.Missing, "Missing")]);
        ConfigurationReadinessScore score = new ConfigurationReadinessScorer().Calculate(evaluation);
        Assert.Equal(50, score.Score);
        Assert.Equal("Orange", score.Grade);
    }
}
