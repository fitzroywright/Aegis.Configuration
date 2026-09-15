using Aegis.Configuration.Domain;

namespace Aegis.Configuration.Application;

public enum SecretProviderKind
{
    None,
    OpenBao,
    Bitwarden,
    HashiCorpVault,
    Environment,
    AppSettings,
    Unknown
}

public sealed record SecretManagerIssue(string Code, string Message, bool IsBlocking = true);

public sealed record SecretManagerStatus(
    SecretProviderKind Provider,
    string ProviderName,
    string Glyph,
    string CssClass,
    int IssueCount,
    IReadOnlyList<SecretManagerIssue> Issues,
    IReadOnlyList<RequirementEvaluation> SecretRequirements,
    string? ManagementUrl,
    string? ConnectionStatus,
    string? CredentialStatus)
{
    public bool HasSecrets => SecretRequirements.Count > 0;
    public bool IsHealthy => HasSecrets && IssueCount == 0 && Provider is not SecretProviderKind.Unknown;
    public bool CanOpenProviderConsole => IsHttpManagementUrl(ManagementUrl);
    public bool CanTestConnection => CanOpenProviderConsole && Provider is SecretProviderKind.OpenBao or SecretProviderKind.HashiCorpVault;
    public bool HasCredentialStatus => !string.IsNullOrWhiteSpace(CredentialStatus);
    public bool CredentialsVerified => HasCredentialStatus && SecretManagerEvaluator.IsPositiveStatus(CredentialStatus!);
    public string IssueLabel => IssueCount switch { 0 => IsHealthy ? "Green" : "No issues", 1 => "1 issue", _ => $"{IssueCount} issues" };

    private static bool IsHttpManagementUrl(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri)) return false;
        return uri.Scheme is Uri.UriSchemeHttp or Uri.UriSchemeHttps;
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

        SecretProviderKind provider = ResolveProvider(contract, secrets);
        var issues = new List<SecretManagerIssue>();

        if (secrets.Length > 0 && provider == SecretProviderKind.Unknown)
            issues.Add(new("provider-unknown", "The application declares secret requirements but does not identify a supported secret provider."));

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

        (string name, string glyph) = ProviderPresentation(provider);
        string css = secrets.Length == 0 ? "neutral" : issues.Count == 0 && provider != SecretProviderKind.Unknown ? "good" : "warn";

        return new SecretManagerStatus(
            provider,
            name,
            glyph,
            css,
            issues.Count,
            issues,
            secrets,
            contract.Secrets?.ManagementUrl,
            connectionStatus,
            credentialStatus);
    }

    private static SecretProviderKind ResolveProvider(ApplicationConfigurationContract contract, IReadOnlyList<RequirementEvaluation> secrets)
    {
        string? declared = contract.Secrets?.Provider;
        if (!string.IsNullOrWhiteSpace(declared)) return ParseProvider(declared);

        string[] sources = secrets.Select(x => x.EffectiveSource)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        SecretProviderKind[] detected = sources.Select(ParseProvider)
            .Where(x => x is not SecretProviderKind.Unknown and not SecretProviderKind.None)
            .Distinct()
            .ToArray();

        if (detected.Length == 1) return detected[0];
        return secrets.Count == 0 ? SecretProviderKind.None : SecretProviderKind.Unknown;
    }

    private static SecretProviderKind ParseProvider(string value)
    {
        string normalized = value.Trim().Replace(" ", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
        if (normalized.Contains("openbao", StringComparison.Ordinal)) return SecretProviderKind.OpenBao;
        if (normalized.Contains("bitwarden", StringComparison.Ordinal)) return SecretProviderKind.Bitwarden;
        if (normalized.Contains("hashicorp", StringComparison.Ordinal) || normalized is "vault" or "hashicorpvault") return SecretProviderKind.HashiCorpVault;
        if (normalized.Contains("environment", StringComparison.Ordinal)) return SecretProviderKind.Environment;
        if (normalized.Contains("appsettings", StringComparison.Ordinal) || normalized.Contains("configuration", StringComparison.Ordinal)) return SecretProviderKind.AppSettings;
        return string.IsNullOrWhiteSpace(normalized) ? SecretProviderKind.None : SecretProviderKind.Unknown;
    }

    internal static bool IsPositiveStatus(string value)
    {
        string normalized = value.Trim().ToLowerInvariant();
        return normalized is "verified" or "healthy" or "ok" or "success" or "available" or "reachable" or "valid" or "configured";
    }

    private static (string Name, string Glyph) ProviderPresentation(SecretProviderKind provider) => provider switch
    {
        SecretProviderKind.OpenBao => ("OpenBao", "🔐"),
        SecretProviderKind.Bitwarden => ("Bitwarden", "🛡"),
        SecretProviderKind.HashiCorpVault => ("HashiCorp Vault", "◆"),
        SecretProviderKind.Environment => ("Environment", "ENV"),
        SecretProviderKind.AppSettings => ("AppSettings", "{}"),
        SecretProviderKind.None => ("No secrets", "—"),
        _ => ("Unknown provider", "?")
    };
}
