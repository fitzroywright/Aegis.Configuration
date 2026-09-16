using System.Text.Encodings.Web;
using Aegis.Configuration.Application;
using Aegis.Configuration.Domain;

internal static class SecretManagerPresentation
{
    internal static string Render(ApplicationConfigurationContract contract, SecretManagerStatus status, string? testMessage = null, bool? testSucceeded = null)
    {
        string appId = Uri.EscapeDataString(contract.ApplicationId);
        int configuredSecrets = status.SecretRequirements.Count(x => x.State is ConfigurationState.Configured or ConfigurationState.Default);
        int requiredSecrets = status.SecretRequirements.Count(x => x.Required);
        string issues = status.Issues.Count == 0
            ? "<div class='empty'>No secret-provider issues are currently reported.</div>"
            : string.Join("", status.Issues.Select(x => $"<div class='secret-issue'><b>{E(x.Code)}</b><span>{E(x.Message)}</span></div>"));
        string requirements = status.SecretRequirements.Count == 0
            ? "<div class='empty'>This application has not declared any secret requirements.</div>"
            : string.Join("", status.SecretRequirements.Select(x => $"<div class='req'><span><b>{E(x.DisplayName)}</b><small>{E(x.Message)}</small></span><code>{E(x.State.ToString())}</code><i class='{(x.State is ConfigurationState.Configured or ConfigurationState.Default ? "ok" : "bad")}'>{(x.State is ConfigurationState.Configured or ConfigurationState.Default ? "✓" : "!")}</i></div>"));

        string console = status.CanOpenProviderConsole
            ? $"<a class='primary' href='{E(status.ManagementUrl)}' target='_blank' rel='noopener'>Open Provider Console</a>"
            : "<span class='disabled-action'>Provider console URL not declared or not HTTP(S)</span>";
        string credentialAction = status.HasCredentialStatus
            ? "<a href='#credentials'>Validate Credentials</a>"
            : "<span class='disabled-action'>Credential validation not published by application</span>";
        string result = string.IsNullOrWhiteSpace(testMessage)
            ? string.Empty
            : $"<div class='probe-result {(testSucceeded == true ? "good" : "warn")}'><b>{(testSucceeded == true ? "Connection test succeeded" : "Connection test failed")}</b><span>{E(testMessage)}</span></div>";
        string credentialClass = status.CredentialsVerified ? "good" : "warn";
        string credentialSummary = status.HasCredentialStatus
            ? $"<div class='probe-result {credentialClass}'><b>{(status.CredentialsVerified ? "Published credential validation is healthy" : "Published credential validation reports an issue")}</b><span>{E(status.CredentialStatus)}</span><small>This status comes from the application's Common.Secrets declaration. Configuration does not retrieve or display the credential value.</small></div>"
            : "<div class='empty'>The application has not published a credential verification status, so Configuration cannot truthfully claim the credentials are valid.</div>";
        string setup = RenderSetup(status, configuredSecrets, requiredSecrets);

        string content = $"""
<style>{Css}</style>
<section class='page-head'><div><h1>{E(contract.DisplayName)} Secrets</h1><p>Common.Secrets capability assistance. Secret values are never stored or displayed here.</p></div></section>
<section class='panel secret-summary'>
  <div class='detail-head'><span class='bigicon'>🔐</span><div><h2>{E(status.ProviderName)}</h2><small>Provider reported through Common.Secrets for {E(contract.ApplicationId)}</small></div><span class='status {(status.IsHealthy ? "good" : "warn")}'>{E(status.IssueLabel)}</span></div>
  <div class='facts'><div><small>PROVIDER</small><b>{E(status.ProviderName)}</b></div><div><small>CONNECTION</small><b>{E(status.ConnectionStatus)}</b></div><div><small>CREDENTIALS</small><b>{E(status.CredentialStatus)}</b></div><div><small>REQUIRED SECRETS</small><b>{requiredSecrets}</b></div></div>
  <div class='secret-actions'><a href='/applications/{appId}'>Back to Configuration</a>{credentialAction}{console}</div>
  {result}
</section>
<section class='panel secret-panel'><h2>Configure</h2><p>Use this checklist to fix the provider setup reported through Common.Secrets. Configuration does not change the provider, store secret values, repair machine identity, or perform Secret Zero bootstrap.</p>{setup}</section>
<section id='credentials' class='panel secret-panel'><h2>Credential Validation</h2>{credentialSummary}</section>
<section id='issues' class='panel secret-panel'><h2>Issues</h2>{issues}</section>
<section class='panel secret-panel'><h2>Declared Secret Requirements</h2>{requirements}</section>
""";
        return ConfigurationShell.Render($"{contract.DisplayName} Secrets", content, "applications");
    }

    private static string RenderSetup(SecretManagerStatus status, int configuredSecrets, int requiredSecrets)
    {
        string endpointState = status.CanOpenProviderConsole ? "ok" : "bad";
        string endpointText = status.CanOpenProviderConsole ? E(status.ManagementUrl) : "No usable HTTP(S) provider endpoint published";
        string connectionState = IsPositive(status.ConnectionStatus) ? "ok" : "bad";
        string connectionText = string.IsNullOrWhiteSpace(status.ConnectionStatus) ? "Not published by application" : E(status.ConnectionStatus);
        string credentialState = status.CredentialsVerified ? "ok" : "bad";
        string credentialText = status.HasCredentialStatus ? E(status.CredentialStatus) : "Not published by application";
        string requirementsState = requiredSecrets == 0 || configuredSecrets >= requiredSecrets ? "ok" : "bad";
        string requirementsText = requiredSecrets == 0 ? "No required secrets declared" : $"{configuredSecrets} of {requiredSecrets} required secret requirements currently configured";

        return $"""
<div class='setup-grid'>
  <div class='setup-step'><i class='{endpointState}'>{(endpointState == "ok" ? "✓" : "!")}</i><span><b>1. Provider endpoint</b><small>{endpointText}</small></span></div>
  <div class='setup-step'><i class='{connectionState}'>{(connectionState == "ok" ? "✓" : "!")}</i><span><b>2. Provider connection</b><small>{connectionText}</small></span></div>
  <div class='setup-step'><i class='{credentialState}'>{(credentialState == "ok" ? "✓" : "!")}</i><span><b>3. Existing credentials</b><small>{credentialText}</small></span></div>
  <div class='setup-step'><i class='{requirementsState}'>{(requirementsState == "ok" ? "✓" : "!")}</i><span><b>4. Required secrets</b><small>{requirementsText}</small></span></div>
</div>
<div class='provider-guidance'><p><b>{E(status.ProviderName)}:</b> provider identity and safe management metadata are supplied through the Common.Secrets capability. Resolve missing requirements in the configured provider. Configuration does not contain provider-specific setup logic.</p></div>
""";
    }

    private static bool IsPositive(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        string normalized = value.Trim().ToLowerInvariant();
        return normalized is "verified" or "healthy" or "ok" or "success" or "available" or "reachable" or "valid" or "configured" or "resolved";
    }

    private static string E(string? value) => HtmlEncoder.Default.Encode(value ?? "—");

    private const string Css = """
.secret-summary,.secret-panel{padding:16px;margin:14px 0}.secret-actions{display:flex;gap:8px;flex-wrap:wrap;margin-top:14px}.secret-actions a,.disabled-action{border:1px solid #22638d;padding:9px 12px;border-radius:5px}.disabled-action{color:#7894a8;opacity:.8}.secret-issue{display:grid;grid-template-columns:180px 1fr;gap:12px;padding:10px;border-bottom:1px solid #153f5c}.secret-issue b{color:#ffbe55}.probe-result{margin-top:14px;padding:12px;border:1px solid;border-radius:6px;display:grid;gap:4px}.probe-result.good{border-color:#0c9e67;background:#063c2b}.probe-result.warn{border-color:#c88a27;background:#4a3411}.probe-result small{color:#9fb8ca}.provider-guidance{color:#c8dceb;margin-top:14px}.setup-grid{display:grid;grid-template-columns:1fr 1fr;gap:8px;margin-top:12px}.setup-step{display:grid;grid-template-columns:28px 1fr;gap:8px;border:1px solid #1f587d;border-radius:6px;padding:10px}.setup-step i{width:22px;height:22px;border-radius:50%;display:grid;place-items:center;font-style:normal}.setup-step i.ok{background:#08764e}.setup-step i.bad{background:#986016}.setup-step small{display:block;color:#9fb8ca;margin-top:2px}.status.neutral{background:#24394a;border-color:#5c7283}@media(max-width:800px){.setup-grid{grid-template-columns:1fr}}
""";
}
