using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Aegis.Configuration.Domain;

internal static class ApplicationPresentation
{
    internal static string RenderDetails(ApplicationConfigurationContract contract, ConfigurationEvaluation evaluation, string rawReport)
    {
        string statusClass = evaluation.IsComplete ? "good" : "warn";
        string statusText = evaluation.IsComplete ? "Configured" : "Not Configured";
        string required = RenderRequirements(evaluation.Requirements.Where(x => x.Required));
        string optional = RenderRequirements(evaluation.Requirements.Where(x => !x.Required));
        string runtime = RenderRuntime(contract.RuntimeEnvironment);
        string diagnostics = RenderDiagnostics(contract.Diagnostics);
        string appId = Uri.EscapeDataString(contract.ApplicationId);

        string content = $"""
<style>{PageCss}</style>
<section class='page-head'><div><h1>{E(contract.DisplayName)}</h1><p>Application configuration details · {E(contract.ApplicationId)}</p></div></section>
<section class='presentation-head panel'>
  <div class='detail-head'><span class='bigicon'>▣</span><div><h2>{E(contract.DisplayName)}</h2><small>{E(contract.Description)}</small></div><span class='status {statusClass}'>{statusText}</span></div>
  <div class='facts presentation-facts'>
    <div><small>VERSION</small><b>{E(contract.Version)}</b></div>
    <div><small>SITE / INSTANCE</small><b>{E(contract.SiteId)} / {E(contract.InstanceId)}</b></div>
    <div><small>LAST REGISTERED</small><b>{Registered(contract.LastRegisteredAtUtc)}</b></div>
    <div><small>APPLICATION ID</small><b>{E(contract.ApplicationId)}</b></div>
  </div>
  <nav class='tabs presentation-tabs'><a href='/applications/{appId}'>Requirements</a><b>Details</b><a href='#diagnostics'>Diagnostics</a><a href='/runbooks/{appId}'>Runbook</a><a href='/applications/{appId}/contract'>Contract</a></nav>
</section>
<section class='presentation-grid'>
  <section class='panel presentation-panel'><h2>Required Configuration</h2>{required}</section>
  <section class='panel presentation-panel'><h2>Optional Configuration</h2>{optional}</section>
</section>
<section id='diagnostics' class='panel presentation-panel'><h2>Diagnostics Capability</h2>{diagnostics}</section>
<section class='panel presentation-panel'><h2>Detected Runtime Environment</h2>{runtime}</section>
<details class='panel raw-section'><summary>Raw commissioning report</summary><p class='muted'>Troubleshooting view. The formatted application details above remain the primary presentation.</p><pre class='raw-box'>{E(rawReport)}</pre></details>
""";
        return ConfigurationShell.Render($"{contract.DisplayName} Details", content, "applications");
    }

    internal static string RenderContract(ApplicationConfigurationContract contract, ConfigurationEvaluation evaluation)
    {
        string statusClass = evaluation.IsComplete ? "good" : "warn";
        string statusText = evaluation.IsComplete ? "Configured" : "Not Configured";
        string appId = Uri.EscapeDataString(contract.ApplicationId);
        string required = RenderContractRequirements(contract.Requirements.Where(x => x.Required));
        string optional = RenderContractRequirements(contract.Requirements.Where(x => !x.Required));
        string diagnostics = RenderDiagnostics(contract.Diagnostics);
        string json = PrettyJson(contract);

        string content = $"""
<style>{PageCss}</style>
<section class='page-head'><div><h1>{E(contract.DisplayName)} Contract</h1><p>Human-readable contract presentation · raw JSON available below for troubleshooting</p></div></section>
<section class='presentation-head panel'>
  <div class='detail-head'><span class='bigicon'>◇</span><div><h2>{E(contract.DisplayName)}</h2><small>{E(contract.ApplicationId)}</small></div><span class='status {statusClass}'>{statusText}</span></div>
  <div class='facts presentation-facts'>
    <div><small>VERSION</small><b>{E(contract.Version)}</b></div>
    <div><small>SITE</small><b>{E(contract.SiteId)}</b></div>
    <div><small>INSTANCE</small><b>{E(contract.InstanceId)}</b></div>
    <div><small>LAST REGISTERED</small><b>{Registered(contract.LastRegisteredAtUtc)}</b></div>
  </div>
  <nav class='tabs presentation-tabs'><a href='/applications/{appId}'>Requirements</a><a href='/applications/{appId}/details'>Details</a><a href='#diagnostics'>Diagnostics</a><a href='/runbooks/{appId}'>Runbook</a><b>Contract</b></nav>
</section>
<section class='presentation-grid'>
  <section class='panel presentation-panel'><h2>Required Contract Items</h2>{required}</section>
  <section class='panel presentation-panel'><h2>Optional Contract Items</h2>{optional}</section>
</section>
<section id='diagnostics' class='panel presentation-panel'><h2>Diagnostics Contract</h2>{diagnostics}</section>
<details class='panel raw-section'><summary>Raw JSON contract</summary><p class='muted'>Machine-readable source. Scroll inside this box rather than replacing the application UI.</p><pre class='raw-box'>{E(json)}</pre></details>
""";
        return ConfigurationShell.Render($"{contract.DisplayName} Contract", content, "applications");
    }

    private static string RenderRequirements(IEnumerable<RequirementEvaluation> requirements)
    {
        RequirementEvaluation[] rows = requirements.ToArray();
        if (rows.Length == 0) return "<div class='empty'>None</div>";
        return "<div class='presentation-table'><table><thead><tr><th>Requirement</th><th>Status</th><th>Commissioning</th><th>Source</th><th>Value</th></tr></thead><tbody>" +
               string.Join("", rows.Select(RenderEvaluationRow)) + "</tbody></table></div>";
    }

    private static string RenderEvaluationRow(RequirementEvaluation r)
    {
        string state = r.State.ToString();
        string css = r.State is ConfigurationState.Configured or ConfigurationState.Default ? "good" : "warn";
        string value = r.Sensitive ? "Protected / not displayed" : E(r.SafeDisplayValue ?? r.DefaultValue);
        string commissioning = string.IsNullOrWhiteSpace(r.VerificationStatus) ? r.Message : r.VerificationStatus;
        return $"<tr><td><b>{E(r.DisplayName)}</b><small>{E(r.Purpose)}</small></td><td><span class='status {css}'>{E(state)}</span></td><td><small>{E(commissioning)}</small></td><td>{E(r.EffectiveSource)}</td><td><code>{value}</code></td></tr>";
    }

    private static string RenderContractRequirements(IEnumerable<ConfigurationRequirement> requirements)
    {
        ConfigurationRequirement[] rows = requirements.ToArray();
        if (rows.Length == 0) return "<div class='empty'>None</div>";
        return "<div class='presentation-table'><table><thead><tr><th>Requirement</th><th>Kind</th><th>Key / Secret</th><th>Declared State</th><th>Commissioning</th></tr></thead><tbody>" +
               string.Join("", rows.Select(r => $"<tr><td><b>{E(r.DisplayName)}</b><small>{E(r.Purpose)}</small></td><td>{E(r.Kind.ToString())}</td><td><code>{E(r.Sensitive ? (r.SecretName ?? r.ConfigurationKey) : (r.ConfigurationKey ?? r.SecretName))}</code></td><td>{E(r.ConfigurationState ?? (r.IsConfigured == true ? "Configured" : r.IsConfigured == false ? "Missing" : "Unknown"))}</td><td><small>{E(r.VerificationStatus)}</small></td></tr>")) +
               "</tbody></table></div>";
    }

    private static string RenderDiagnostics(DiagnosticsCapability? d)
    {
        if (d is null) return "<div class='empty'>This application did not declare a diagnostics capability.</div>";
        string levels = d.SupportedLevels is { Count: > 0 } ? string.Join(", ", d.SupportedLevels) : "1, 2, 3, 4, 5";
        return $"<div class='facts presentation-facts'><div><small>BASE URL KEY</small><b>{E(d.BaseUrlConfigurationKey)}</b></div><div><small>HEALTH</small><b>{E(d.HealthPath)}</b></div><div><small>REMOTE DIAGNOSTICS</small><b>{YesNo(d.SupportsRemoteDiagnostics)}</b></div><div><small>OPERATIONAL TELEMETRY</small><b>{YesNo(d.SupportsOperationalTelemetry)}</b></div><div><small>AUTHENTICATION</small><b>{E(d.AuthenticationScheme)}</b></div><div><small>SUPPORTED LEVELS</small><b>{E(levels)}</b></div></div>";
    }

    private static string RenderRuntime(RuntimeEnvironmentSnapshot? r)
    {
        if (r is null) return "<div class='empty'>No runtime environment snapshot has been published.</div>";
        string interfaces = r.NetworkInterfaces is not { Count: > 0 } ? "<div class='empty'>No network interfaces reported.</div>" :
            "<div class='presentation-table'><table><thead><tr><th>Interface</th><th>Type</th><th>Addresses</th><th>Gateways</th></tr></thead><tbody>" +
            string.Join("", r.NetworkInterfaces.Select(n => $"<tr><td>{E(n.Name)}</td><td>{E(n.Type)}</td><td><code>{E(n.Addresses is { Count: > 0 } ? string.Join(", ", n.Addresses) : null)}</code></td><td>{E(n.Gateways is { Count: > 0 } ? string.Join(", ", n.Gateways) : null)}</td></tr>")) + "</tbody></table></div>";
        string storage = r.Storage is not { Count: > 0 } ? "<div class='empty'>No storage identity reported.</div>" :
            "<div class='presentation-table'><table><thead><tr><th>Volume</th><th>Type</th><th>Format</th></tr></thead><tbody>" +
            string.Join("", r.Storage.Select(s => $"<tr><td>{E(s.Name)}</td><td>{E(s.DriveType)}</td><td>{E(s.Format)}</td></tr>")) + "</tbody></table></div>";
        return $"<div class='facts presentation-facts'><div><small>MACHINE / HOST</small><b>{E(r.MachineName)} / {E(r.HostName)}</b></div><div><small>ENVIRONMENT</small><b>{E(r.HostingEnvironment)}</b></div><div><small>OPERATING SYSTEM</small><b>{E(r.OperatingSystem)}</b></div><div><small>ARCHITECTURE</small><b>{E(r.OsArchitecture)} / {E(r.ProcessArchitecture)}</b></div><div><small>.NET</small><b>{E(r.FrameworkDescription)}</b></div><div><small>DNS DOMAIN</small><b>{E(r.DnsDomainName)}</b></div></div><h3>Network Identity</h3>{interfaces}<h3>Storage Identity</h3>{storage}";
    }

    private static string PrettyJson(ApplicationConfigurationContract contract)
    {
        JsonSerializerOptions options = new() { WriteIndented = true };
        options.Converters.Add(new JsonStringEnumConverter());
        return JsonSerializer.Serialize(contract, options);
    }

    private static string Registered(DateTimeOffset? value) => value.HasValue ? value.Value.ToString("MMM d, yyyy HH:mm") + " UTC" : "Never";
    private static string YesNo(bool value) => value ? "Yes" : "No";
    private static string E(string? value) => HtmlEncoder.Default.Encode(value ?? "—");

    private const string PageCss = """
.presentation-head{padding:16px;margin:14px 0}.presentation-grid{display:grid;grid-template-columns:1fr 1fr;gap:14px;margin:14px 0}.presentation-panel{padding:16px;margin:14px 0;min-width:0}.presentation-grid .presentation-panel{margin:0}.presentation-panel h2{margin-top:0}.presentation-facts{grid-template-columns:repeat(4,minmax(0,1fr));gap:0}.presentation-facts b{overflow-wrap:anywhere}.presentation-tabs{margin-top:12px}.presentation-table{overflow:auto;border:1px solid #153f5c;border-radius:6px}.presentation-table table{min-width:920px}.presentation-table td{white-space:normal;vertical-align:top}.presentation-table code{color:#80d5ff;overflow-wrap:anywhere}.raw-section{padding:16px;margin:14px 0}.raw-section summary{cursor:pointer;color:#7dcaff;font-weight:800;font-size:15px}.raw-box{display:block;width:100%;max-width:100%;max-height:520px;overflow:auto;white-space:pre;font:12px/1.5 Consolas,'Courier New',monospace;background:#000b13;border:1px solid #275b7c;border-radius:6px;padding:16px;color:#cfe8fa;margin-top:12px}@media(max-width:1000px){.presentation-grid{grid-template-columns:1fr}.presentation-facts{grid-template-columns:1fr 1fr}}@media(max-width:650px){.presentation-facts{grid-template-columns:1fr}}
""";
}
