using Aegis.Configuration.Domain;

namespace Aegis.Configuration.Application;

public sealed record ConfigurationReadinessScore(int Score, int Verified, int TotalRequired, string Grade);

public sealed class ConfigurationReadinessScorer
{
    public ConfigurationReadinessScore Calculate(ConfigurationEvaluation evaluation)
    {
        RequirementEvaluation[] required = evaluation.Requirements.Where(x => x.Required).ToArray();
        if (required.Length == 0) return new(100, 0, 0, "Green");
        double points = required.Sum(x => x.State switch
        {
            ConfigurationState.Verified => 1.0,
            ConfigurationState.ConfiguredUnverified => 0.6,
            ConfigurationState.Invalid => 0.2,
            _ => 0.0
        });
        int score = (int)Math.Round(points / required.Length * 100, MidpointRounding.AwayFromZero);
        string grade = score switch { >= 90 => "Green", >= 70 => "Yellow", >= 40 => "Orange", _ => "Red" };
        return new(score, required.Count(x => x.State == ConfigurationState.Verified), required.Length, grade);
    }
}
