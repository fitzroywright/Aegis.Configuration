using Aegis.Configuration.Domain;

namespace Aegis.Configuration.Tests;

public sealed class ConfigurationReadinessTests
{
    [Fact]
    public void Readiness_counts_required_and_optional_without_exposing_values()
    {
        var evaluation = new ConfigurationEvaluation(
            "Aegis.Example",
            "1.0",
            [
                new RequirementEvaluation("a","Database",true,ConfigurationState.Configured,"Configured."),
                new RequirementEvaluation("b","API Key",true,ConfigurationState.Missing,"Missing.",Sensitive:true),
                new RequirementEvaluation("c","Optional Feature",false,ConfigurationState.Disabled,"Disabled."),
                new RequirementEvaluation("d","Optional URL",false,ConfigurationState.OptionalNotConfigured,"Missing.")
            ]);

        ConfigurationReadiness readiness = evaluation.ToReadiness();

        Assert.Equal(1, readiness.RequiredPresent);
        Assert.Equal(2, readiness.RequiredTotal);
        Assert.Equal(1, readiness.OptionalPresent);
        Assert.Equal(2, readiness.OptionalTotal);
        Assert.Equal(["API Key"], readiness.MissingRequiredNames);
        Assert.False(readiness.IsReady);
    }

    [Fact]
    public void Disabled_required_setting_is_not_ready()
    {
        var evaluation = new ConfigurationEvaluation(
            "Aegis.Example",
            "1.0",
            [new RequirementEvaluation("x","Required Feature",true,ConfigurationState.Disabled,"Disabled.")]);

        Assert.False(evaluation.ToReadiness().IsReady);
    }

    [Fact]
    public void Unknown_required_setting_is_not_ready()
    {
        var evaluation = new ConfigurationEvaluation(
            "Aegis.Example",
            "1.0",
            [new RequirementEvaluation("x","Unknown",true,ConfigurationState.Unknown,"Unknown.")]);

        Assert.False(evaluation.ToReadiness().IsReady);
    }
}
