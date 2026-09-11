namespace Aegis.Configuration.Application;

public sealed record RecoveryVerification(
    bool Recovered,
    int PreviousRisk,
    int CurrentRisk,
    IReadOnlyList<string> RemainingApplications,
    string Message);

public sealed class RecoveryVerifier
{
    public RecoveryVerification Verify(OperationalRiskReport before, OperationalRiskReport after)
    {
        int previous = before.Items.Sum(x => x.Priority);
        int current = after.Items.Sum(x => x.Priority);
        string[] remaining = after.Items.Where(x => x.Level != OperationalRiskLevel.Healthy).Select(x => x.ApplicationId).Distinct().ToArray();
        bool recovered = remaining.Length == 0;
        string message = recovered
            ? "Recovery verified: no applications remain at operational risk."
            : current < previous
                ? $"Recovery is progressing: aggregate risk reduced from {previous} to {current}."
                : $"Recovery is not yet proven: aggregate risk is {current} versus {previous} before remediation.";
        return new(recovered, previous, current, remaining, message);
    }
}
