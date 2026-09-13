using Aegis.Configuration.Domain;

namespace Aegis.Configuration.Application;

public sealed record ConfigurationReadinessScore(int Score, int Configured, int TotalRequired, string Grade);

public sealed class ConfigurationReadinessScorer
{
    public ConfigurationReadinessScore Calculate(ConfigurationEvaluation evaluation)
    {
        RequirementEvaluation[] required = evaluation.Requirements.Where(x => x.Required).ToArray();
        if (required.Length == 0) return new(100, 0, 0, "Green");

        int configured = required.Count(x => x.State == ConfigurationState.Configured);
        int score = (int)Math.Round((double)configured / required.Length * 100, MidpointRounding.AwayFromZero);
        string grade = score switch { 100 => "Green", >= 75 => "Yellow", >= 50 => "Orange", _ => "Red" };
        return new(score, configured, required.Length, grade);
    }
}
