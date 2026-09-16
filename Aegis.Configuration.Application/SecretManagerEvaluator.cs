using Aegis.Configuration.Domain;

namespace Aegis.Configuration.Application;

public sealed record SecretManagerIssue(string Code, string Message, bool IsBlocking = true);

public sealed record SecretManagerStatus(
    string ProviderName,
    int IssueCount,
    IReadOnlyList<SecretManagerIssue> Issues,
    IReadOnlyList<RequirementEvaluation> SecretRequirements,
    string? ManagementUrl,
    string? ConnectionStatus,
    string? CredentialStatus)
{
    public bool HasSecrets => SecretRequirements.Count > 0;
    public bool IsHealthy => HasSecrets && IssueCount == 0 && !string.Equals(ProviderName, "Unknown provider", StringComparison.OrdinalIgnoreCase);
    public bool CanOpenProviderConsole => IsHttpManagementUrl(ManagementUrl);
    // Configuration cannot invent provider-specific health paths. A future Common.Secrets capability may publish one explicitly.
    public bool CanTestConnection => false;
    public bool HasCredentialStatus => !string.IsNullOrWhiteSpace(CredentialStatus);
    public bool CredentialsVerified => HasCredentialStatus && SecretManagerEvaluator.IsPositiveStatus(CredentialStatus!);
    public string IssueLabel => IssueCount switch { 0 => IsHealthy ? "Green" : "No issues", 1 => "1 issue", _ => $"{IssueCount} issues" };
    public string Glyph => HasSecrets ? "🔐" : "—";
    public string CssClass => !HasSecrets ? "neutral" : IsHealthy ? "good" : "warn";

    private static bool IsHttpManagementUrl(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri)) return false;
        return string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            || string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
    }
}

public static class SecretManagerEvaluator
{
    public static SecretManagerStatus Evaluate(ApplicationConfigurationContract contract, ConfigurationEvaluation evaluation)
    {
        HashSet<string> secretIds = contract.Requirements
            .Where(x => x.Kind == ConfigurationRequirementKind.Secret)
            .Select(x => x.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        RequirementEvaluation[] secrets = evaluation.Requirements
            .Where(x => secretIds.Contains(x.RequirementId))
            .ToArray();

        string providerName = ResolveProviderName(contract, secrets.Length > 0);
        var issues = new List<SecretManagerIssue>();

        if (secrets.Length > 0 && string.Equals(providerName, "Unknown provider", StringComparison.OrdinalIgnoreCase))
            issues.Add(new("provider-unknown", "The application declares secret requirements but does not explicitly declare its Common.Secrets provider."));

        foreach (RequirementEvaluation secret in secrets.Where(x => x.Required))
        {
            if (secret.State is not (ConfigurationState.Configured or ConfigurationState.Default))
                issues.Add(new($"secret-{secret.RequirementId}", $"{secret.DisplayName}: {secret.Message}"));

            if (!string.IsNullOrWhiteSpace(secret.VerificationStatus) && !IsPositiveStatus(secret.VerificationStatus))
                issues.Add(new($"verification-{secret.RequirementId}", $"{secret.DisplayName}: verification reports '{secret.VerificationStatus}'."));
        }

        string? connectionStatus = contract.Secrets?.ConnectionStatus;
        if (!string.IsNullOrWhiteSpace(connectionStatus) && !IsPositiveStatus(connectionStatus))
            issues.Add(new("provider-connection", $"Provider connection status is '{connectionStatus}'."));

        string? credentialStatus = contract.Secrets?.CredentialStatus;
        if (!string.IsNullOrWhiteSpace(credentialStatus) && !IsPositiveStatus(credentialStatus))
            issues.Add(new("provider-credentials", $"Provider credential status is '{credentialStatus}'."));

        return new SecretManagerStatus(
            providerName,
            issues.Count,
            issues,
            secrets,
            contract.Secrets?.ManagementUrl,
            connectionStatus,
            credentialStatus);
    }

    private static string ResolveProviderName(ApplicationConfigurationContract contract, bool hasSecrets)
    {
        if (!hasSecrets) return "No secrets";
        string? declared = contract.Secrets?.Provider;
        return string.IsNullOrWhiteSpace(declared) ? "Unknown provider" : declared.Trim();
    }

    public static bool IsPositiveStatus(string value)
    {
        string normalized = value.Trim().ToLowerInvariant();
        return normalized is "verified" or "healthy" or "ok" or "success" or "available" or "reachable" or "valid" or "configured" or "resolved";
    }
}
