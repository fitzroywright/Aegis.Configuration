using System.Text.Encodings.Web;
using Aegis.Configuration.Application;
using Aegis.Configuration.Domain;

internal static class SecretManagerPresentation
{
    internal static string Render(ApplicationConfigurationContract contract, SecretManagerStatus status, string? testMessage = null, bool? testSucceeded = null)
    {
        string appId = Uri.EscapeDataString(contract.ApplicationId);
        string issues = status.Issues.Count == 0
            ? "<div class='empty'>No secret-provider issues are currently reported.</div>"
            : string.Join("", status.Issues.Select(x => $"<div class='secret-issue'><b>{E(x.Code)}</b><span>{E(x.Message)}</span></div>"));
        string requirements = status.SecretRequirements.Count == 0
            ? "<div class='empty'>This application has not declared any secret requirements.</div>"
            : string.Join("", status.SecretRequirements.Select(x => $"<div class='req'><span><b>{E(x.DisplayName)}</b><small>{E(x.Message)}</small></span><code>{E(x.State.ToString())}</code><i class='{(x.State is ConfigurationState.Configured or ConfigurationState.Default ? "ok" : "bad")}'>{(x.State is ConfigurationState.Configured or ConfigurationState.Default ? "✓" : "!")}</i></div>"));

        string console = status.CanOpenProviderConsole
            ? $"<a class='primary' href='{E(status.ManagementUrl)}' target='_blank' rel='noopener'>Open Provider Console</a>"
            : "<span class='disabled-action'>Provider console URL not declared or not HTTP(S)</span>";
        string test = status.CanTestConnection
            ? $"<a href='/applications/{appId}/secrets/test'>Test Connection</a>"
            : "<span class='disabled-action'>Connection test unavailable for this declaration</span>";
        string credentialAction = status.HasCredentialStatus
            ? "<a href='#credentials'>Validate Credentials</a>"
            : "<span class='disabled-action'>Credential validation not published by application</span>";
        string result = string.IsNullOrWhiteSpace(testMessage)
            ? string.Empty
            : $"<div class='probe-result {(testSucceeded == true ? "good" : "warn")}'><b>{(testSucceeded == true ? "Connection test succeeded" : "Connection test failed")}</b><span>{E(testMessage)}</span></div>";
        string credentialClass = status.CredentialsVerified ? "good" : "warn";
        string credentialSummary = status.HasCredentialStatus
            ? $"<div class='probe-result {credentialClass}'><b>{(status.CredentialsVerified ? "Published credential validation is healthy" : "Published credential validation reports an issue")}</b><span>{E(status.CredentialStatus)}</span><small>This status comes from the application's own configuration contract. Configuration does not retrieve or display the credential value.</small></div>"
            : "<div class='empty'>The application has not published a credential verification status, so Configuration cannot truthfully claim the credentials are valid.</div>";

        string content = $"""
<style>{Css}</style>
<section class='page-head'><div><h1>{E(contract.DisplayName)} Secrets</h1><p>Provider-aware configuration assistance. Secret values are never stored or displayed here.</p></div></section>
<section class='panel secret-summary'>
  <div class='detail-head'><span class='bigicon'>{E(status.Glyph)}</span><div><h2>{E(status.ProviderName)}</h2><small>Declared or inferred secret provider for {E(contract.ApplicationId)}</small></div><span class='status {E(status.CssClass)}'>{E(status.IssueLabel)}</span></div>
  <div class='facts'><div><small>PROVIDER</small><b>{E(status.ProviderName)}</b></div><div><small>CONNECTION</small><b>{E(status.ConnectionStatus)}</b></div><div><small>CREDENTIALS</small><b>{E(status.CredentialStatus)}</b></div><div><small>REQUIRED SECRETS</small><b>{status.SecretRequirements.Count(x => x.Required)}</b></div></div>
  <div class='secret-actions'><a href='/applications/{appId}'>Back to Configuration</a>{test}{credentialAction}{console}</div>
  {result}
</section>
<section class='panel secret-panel'><h2>Configure</h2><p>Configuration may guide and validate the provider declared by the application. It does not change the selected provider and does not become a secret store.</p><div class='provider-guidance'>{ProviderGuidance(status.Provider)}</div></section>
<section id='credentials' class='panel secret-panel'><h2>Credential Validation</h2>{credentialSummary}</section>
<section id='issues' class='panel secret-panel'><h2>Issues</h2>{issues}</section>
<section class='panel secret-panel'><h2>Declared Secret Requirements</h2>{requirements}</section>
""";
        return ConfigurationShell.Render($"{contract.DisplayName} Secrets", content, "applications");
    }

    private static string ProviderGuidance(SecretProviderKind provider) => provider switch
    {
        SecretProviderKind.OpenBao => "<p>OpenBao setup can validate the declared endpoint, current provider state and required secret access. Machine-identity repair and Secret Zero are intentionally out of scope.</p>",
        SecretProviderKind.Bitwarden => "<p>Bitwarden setup currently validates the application declaration and secret requirement state. Provider provisioning is not performed by Configuration.</p>",
        SecretProviderKind.HashiCorpVault => "<p>HashiCorp Vault setup can validate the declared endpoint and required secret state. Provider changes and bootstrap identity work are not performed here.</p>",
        SecretProviderKind.Environment => "<p>Environment-backed secrets are validated from the application's published requirement state. Configuration does not write environment variables to the target host.</p>",
        SecretProviderKind.AppSettings => "<p>AppSettings-backed secrets are reported and reviewed here. Configuration does not write secret values into application settings.</p>",
        SecretProviderKind.None => "<p>No secret provider is required by the current contract.</p>",
        _ => "<p>The application declares secrets but the provider could not be identified. Publish an optional Secrets provider declaration or a recognizable effective source.</p>"
    };

    private static string E(string? value) => HtmlEncoder.Default.Encode(value ?? "—");

    private const string Css = """
.secret-summary,.secret-panel{padding:16px;margin:14px 0}.secret-actions{display:flex;gap:8px;flex-wrap:wrap;margin-top:14px}.secret-actions a,.disabled-action{border:1px solid #22638d;padding:9px 12px;border-radius:5px}.disabled-action{color:#7894a8;opacity:.8}.secret-issue{display:grid;grid-template-columns:180px 1fr;gap:12px;padding:10px;border-bottom:1px solid #153f5c}.secret-issue b{color:#ffbe55}.probe-result{margin-top:14px;padding:12px;border:1px solid;border-radius:6px;display:grid;gap:4px}.probe-result.good{border-color:#0c9e67;background:#063c2b}.probe-result.warn{border-color:#c88a27;background:#4a3411}.probe-result small{color:#9fb8ca}.provider-guidance{color:#c8dceb}.status.neutral{background:#24394a;border-color:#5c7283}
""";
}
