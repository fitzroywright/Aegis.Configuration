using Aegis.Configuration.Application;
using Aegis.Configuration.Domain;

namespace Aegis.Configuration.Tests;

public sealed class SecretManagerEvaluatorTests
{
    [Fact]
    public void Uses_explicit_openbao_declaration()
    {
        var contract = Contract(
            new SecretProviderDeclaration("OpenBao", "https://openbao.example:8200", "Healthy", "Valid"),
            new ConfigurationRequirement("db", "Database", ConfigurationRequirementKind.Secret, true, "Database credential", IsConfigured: true, EffectiveSource: "OpenBao", VerificationStatus: "Verified"));
        var evaluation = Evaluation(new RequirementEvaluation("db", "Database", true, ConfigurationState.Configured, "Configured.", EffectiveSource: "OpenBao", VerificationStatus: "Verified"));

        SecretManagerStatus result = SecretManagerEvaluator.Evaluate(contract, evaluation);

        Assert.Equal(SecretProviderKind.OpenBao, result.Provider);
        Assert.True(result.IsHealthy);
        Assert.Equal(0, result.IssueCount);
        Assert.True(result.CanTestConnection);
        Assert.True(result.HasCredentialStatus);
        Assert.True(result.CredentialsVerified);
    }

    [Fact]
    public void Requires_explicit_provider_when_secrets_are_declared()
    {
        var contract = Contract(null,
            new ConfigurationRequirement("graph", "Graph", ConfigurationRequirementKind.Secret, true, "Graph secret", IsConfigured: true, EffectiveSource: "Bitwarden Secrets Manager"));
        var evaluation = Evaluation(new RequirementEvaluation("graph", "Graph", true, ConfigurationState.Configured, "Configured.", EffectiveSource: "Bitwarden Secrets Manager"));

        SecretManagerStatus result = SecretManagerEvaluator.Evaluate(contract, evaluation);

        Assert.Equal(SecretProviderKind.Unknown, result.Provider);
        Assert.Equal(1, result.IssueCount);
        Assert.Contains(result.Issues, x => x.Code == "provider-unknown");
    }

    [Fact]
    public void Reports_missing_required_secret_as_issue()
    {
        var contract = Contract(new SecretProviderDeclaration("OpenBao"),
            new ConfigurationRequirement("signing", "Signing Key", ConfigurationRequirementKind.Secret, true, "Signs messages", IsConfigured: false, EffectiveSource: "OpenBao"));
        var evaluation = Evaluation(new RequirementEvaluation("signing", "Signing Key", true, ConfigurationState.Missing, "Required value is unresolved and no effective default is available.", EffectiveSource: "OpenBao"));

        SecretManagerStatus result = SecretManagerEvaluator.Evaluate(contract, evaluation);

        Assert.Equal(1, result.IssueCount);
        Assert.False(result.IsHealthy);
        Assert.Contains(result.Issues, x => x.Code == "secret-signing");
    }

    [Fact]
    public void Does_not_invent_provider_when_application_has_no_secrets()
    {
        var contract = Contract(null,
            new ConfigurationRequirement("url", "Service URL", ConfigurationRequirementKind.Configuration, true, "Endpoint", IsConfigured: true));
        var evaluation = Evaluation(new RequirementEvaluation("url", "Service URL", true, ConfigurationState.Configured, "Configured."));

        SecretManagerStatus result = SecretManagerEvaluator.Evaluate(contract, evaluation);

        Assert.Equal(SecretProviderKind.None, result.Provider);
        Assert.False(result.HasSecrets);
        Assert.Equal(0, result.IssueCount);
    }

    [Fact]
    public void Rejects_non_http_provider_console_urls()
    {
        var contract = Contract(
            new SecretProviderDeclaration("OpenBao", "javascript:alert(1)", "Healthy", "Valid"),
            new ConfigurationRequirement("db", "Database", ConfigurationRequirementKind.Secret, true, "Database credential", IsConfigured: true, EffectiveSource: "OpenBao"));
        var evaluation = Evaluation(new RequirementEvaluation("db", "Database", true, ConfigurationState.Configured, "Configured.", EffectiveSource: "OpenBao"));

        SecretManagerStatus result = SecretManagerEvaluator.Evaluate(contract, evaluation);

        Assert.False(result.CanOpenProviderConsole);
        Assert.False(result.CanTestConnection);
    }

    [Fact]
    public void Reports_unverified_published_credential_state_as_issue()
    {
        var contract = Contract(
            new SecretProviderDeclaration("OpenBao", "https://openbao.example:8200", "Healthy", "Invalid"),
            new ConfigurationRequirement("db", "Database", ConfigurationRequirementKind.Secret, true, "Database credential", IsConfigured: true, EffectiveSource: "OpenBao"));
        var evaluation = Evaluation(new RequirementEvaluation("db", "Database", true, ConfigurationState.Configured, "Configured.", EffectiveSource: "OpenBao"));

        SecretManagerStatus result = SecretManagerEvaluator.Evaluate(contract, evaluation);

        Assert.True(result.HasCredentialStatus);
        Assert.False(result.CredentialsVerified);
        Assert.Contains(result.Issues, x => x.Code == "provider-credentials");
    }

    private static ApplicationConfigurationContract Contract(SecretProviderDeclaration? secrets, params ConfigurationRequirement[] requirements)
        => new("Test.App", "Test App", "1.0", requirements, Secrets: secrets);

    private static ConfigurationEvaluation Evaluation(params RequirementEvaluation[] requirements)
        => new("Test.App", "1.0", requirements);
}
