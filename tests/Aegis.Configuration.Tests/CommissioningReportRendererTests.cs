using Aegis.Configuration.Application;
using Aegis.Configuration.Domain;

namespace Aegis.Configuration.Tests;

public sealed class CommissioningReportRendererTests
{
    [Fact]
    public void RenderMarkdown_DoesNotContainSecretValue()
    {
        ConfigurationEvaluation evaluation = new(false, [new("secret", "Admin Key", ConfigurationState.Verified, "Secret resolved successfully.")]);
        CommissioningSnapshot snapshot = new("demo", "1.0", DateTimeOffset.UtcNow, evaluation);
        string report = new CommissioningReportRenderer().RenderMarkdown(snapshot);
        Assert.Contains("Secret values are intentionally excluded", report);
        Assert.DoesNotContain("super-secret-value", report);
    }
}
