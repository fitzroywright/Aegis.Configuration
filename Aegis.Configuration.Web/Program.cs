using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json.Serialization;
using Aegis.Configuration.Application;
using Aegis.Configuration.Domain;
using Aegis.Configuration.Infrastructure;
using Common.Diagnostics;
using Common.Secrets;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
string contractDirectory = builder.Configuration["AegisConfiguration:ContractDirectory"] ?? Path.Combine(builder.Environment.ContentRootPath, "contracts");
string instructionDirectory = builder.Configuration["AegisConfiguration:InstructionDirectory"] ?? Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "..", "..", "instruction-packs"));
builder.Services.AddCommonSecrets(builder.Configuration);
builder.Services.AddSuiteSecurity();
DirectoryContractStore contractStore = new(contractDirectory);
builder.Services.AddSingleton<IConfigurationContractStore>(contractStore);
builder.Services.AddSingleton<IConfigurationContractRegistrationStore>(contractStore);
builder.Services.AddSingleton(new InstructionPackCatalog(instructionDirectory));
builder.Services.AddSingleton<ConfigurationEvaluator>();
builder.Services.AddSingleton<CommissioningService>();
builder.Services.AddSingleton<CommissioningReportRenderer>();
builder.Services.AddSingleton<ContractValidator>();
WebApplication app = builder.Build();
app.UseSuiteSecurity();
app.MapSuiteSecurity();

app.MapGet("/", async (IConfigurationContractStore store, ConfigurationEvaluator evaluator, CancellationToken ct) =>
{
    var contracts = (await store.LoadAllAsync(ct)).OrderBy(x => x.DisplayName).ToList();
    var rows = new List<(ApplicationConfigurationContract Contract, ConfigurationEvaluation Evaluation)>();
    foreach (var c in contracts) rows.Add((c, await evaluator.EvaluateAsync(c, ct)));
    return Html(RenderDashboard(rows, null));
});

app.MapGet("/applications/{applicationId}", async (string applicationId, IConfigurationContractStore store, ConfigurationEvaluator evaluator, CancellationToken ct) =>
{
    var contracts = (await store.LoadAllAsync(ct)).OrderBy(x => x.DisplayName).ToList();
    var rows = new List<(ApplicationConfigurationContract Contract, ConfigurationEvaluation Evaluation)>();
    foreach (var c in contracts) rows.Add((c, await evaluator.EvaluateAsync(c, ct)));
    int selected = rows.FindIndex(x => string.Equals(x.Contract.ApplicationId, applicationId, StringComparison.OrdinalIgnoreCase));
    return selected < 0 ? Results.NotFound() : Html(RenderDashboard(rows, selected));
});

app.MapGet("/applications/{applicationId}/details", async (string applicationId, IConfigurationContractStore store, ConfigurationEvaluator evaluator, CommissioningReportRenderer renderer, CancellationToken ct) =>
{
    var contract = (await store.LoadAllAsync(ct)).FirstOrDefault(x => string.Equals(x.ApplicationId, applicationId, StringComparison.OrdinalIgnoreCase));
    if (contract is null) return Results.NotFound();
    ConfigurationEvaluation evaluation = await evaluator.EvaluateAsync(contract, ct);
    CommissioningSnapshot snapshot = new(contract.ApplicationId, contract.Version, DateTimeOffset.UtcNow, evaluation);
    string rawReport = renderer.RenderMarkdown(snapshot);
    return Html(ApplicationPresentation.RenderDetails(contract, evaluation, rawReport));
});

app.MapGet("/applications/{applicationId}/contract", async (string applicationId, IConfigurationContractStore store, ConfigurationEvaluator evaluator, CancellationToken ct) =>
{
    var contract = (await store.LoadAllAsync(ct)).FirstOrDefault(x => string.Equals(x.ApplicationId, applicationId, StringComparison.OrdinalIgnoreCase));
    if (contract is null) return Results.NotFound();
    ConfigurationEvaluation evaluation = await evaluator.EvaluateAsync(contract, ct);
    return Html(ApplicationPresentation.RenderContract(contract, evaluation));
});

app.MapGet("/applications/{applicationId}/secrets", async (string applicationId, IConfigurationContractStore store, ConfigurationEvaluator evaluator, CancellationToken ct) =>
{
    var contract = (await store.LoadAllAsync(ct)).FirstOrDefault(x => string.Equals(x.ApplicationId, applicationId, StringComparison.OrdinalIgnoreCase));
    if (contract is null) return Results.NotFound();
    ConfigurationEvaluation evaluation = await evaluator.EvaluateAsync(contract, ct);
    SecretManagerStatus status = SecretManagerEvaluator.Evaluate(contract, evaluation);
    return Html(SecretManagerPresentation.Render(contract, status));
});

app.MapGet("/applications/{applicationId}/secrets/test", async (string applicationId, IConfigurationContractStore store, ConfigurationEvaluator evaluator, CancellationToken ct) =>
{
    var contract = (await store.LoadAllAsync(ct)).FirstOrDefault(x => string.Equals(x.ApplicationId, applicationId, StringComparison.OrdinalIgnoreCase));
    if (contract is null) return Results.NotFound();
    ConfigurationEvaluation evaluation = await evaluator.EvaluateAsync(contract, ct);
    SecretManagerStatus status = SecretManagerEvaluator.Evaluate(contract, evaluation);
    string message = string.IsNullOrWhiteSpace(status.ConnectionStatus)
        ? "No provider-neutral health result has been published by this application."
        : $"Application-published Common.Secrets status: {status.ConnectionStatus}.";
    bool? healthy = string.IsNullOrWhiteSpace(status.ConnectionStatus) ? null : SecretManagerEvaluator.IsPositiveStatus(status.ConnectionStatus);
    return Html(SecretManagerPresentation.Render(contract, status, message, healthy));
});

app.MapGet("/runbooks", async (IConfigurationContractStore store, CancellationToken ct) =>
{
    var contracts = await store.LoadAllAsync(ct);
    string cards = contracts.Count == 0
        ? "<div class='empty'>No runbooks available.</div>"
        : string.Join("", contracts.OrderBy(x => x.DisplayName).Select(x => $"<a class='runbook-card' href='/runbooks/{Uri.EscapeDataString(x.ApplicationId)}'><span class='appicon'>▤</span><span><b>{E(x.DisplayName)}</b><small>Version {E(x.Version)}</small></span><span>›</span></a>"));
    return Html(ConfigurationShell.Render("Runbooks", $"<section class='page-head'><div><h1>Runbooks</h1><p>Configuration commissioning and recovery instructions</p></div></section><section class='panel'><h2>Configuration Runbooks</h2>{cards}</section>", "runbooks"));
});

app.MapGet("/runbooks/{applicationId}", async (string applicationId, IConfigurationContractStore store, CancellationToken ct) =>
{
    var contract = (await store.LoadAllAsync(ct)).FirstOrDefault(x => string.Equals(x.ApplicationId, applicationId, StringComparison.OrdinalIgnoreCase));
    if (contract is null) return Results.NotFound();
    return Html(ConfigurationShell.Render("Runbook", $"<section class='page-head'><div><h1>{E(contract.DisplayName)} Runbook</h1><p>Commissioning guidance for {E(contract.ApplicationId)}</p></div></section><section class='panel'><h2>Configuration Contract</h2><p>Version {E(contract.Version)} · Site {E(contract.SiteId)} · Instance {E(contract.InstanceId)}</p><a class='primary' href='/applications/{Uri.EscapeDataString(contract.ApplicationId)}'>Back to application</a></section>", "runbooks"));
});

app.MapPost("/api/contracts/register", async (HttpContext context, ApplicationConfigurationContract contract, IConfigurationContractRegistrationStore store, ContractValidator validator, IConfiguration configuration, ILoggerFactory loggerFactory, CancellationToken ct) =>
{
    ILogger registrationLogger = loggerFactory.CreateLogger("Aegis.Configuration.Registration");
    string attemptId = context.Request.Headers["X-Aegis-Registration-Attempt-Id"].ToString();
    if (string.IsNullOrWhiteSpace(attemptId)) attemptId = Guid.NewGuid().ToString("N");
    using IDisposable? logScope = registrationLogger.BeginScope(new Dictionary<string, object?>
    {
        ["Activity"] = "Registration",
        ["Application"] = contract.ApplicationId,
        ["RegistrationAttemptId"] = attemptId,
        ["Stage"] = "ContractRegistration"
    });
    registrationLogger.LogInformation("Configuration contract registration request received.");
    string? configuredKey = configuration["AegisConfiguration:RegistrationKey"];
    if (string.IsNullOrWhiteSpace(configuredKey))
    {
        registrationLogger.LogWarning("Configuration contract registration cannot continue because the Operations registration credential is not configured.");
        return Results.Problem("Application registration is disabled because no registration credential is configured.", statusCode: StatusCodes.Status503ServiceUnavailable);
    }
    string supplied = context.Request.Headers["X-Aegis-Registration-Key"].ToString();
    if (!FixedEquals(configuredKey, supplied))
    {
        registrationLogger.LogWarning("Configuration contract registration rejected because the supplied credential is invalid.");
        return Results.Unauthorized();
    }
    registrationLogger.LogInformation("Configuration registration credential accepted.");
    var validation = validator.Validate(contract);
    if (!validation.IsValid)
    {
        registrationLogger.LogWarning("Configuration contract validation failed.");
        return Results.BadRequest(validation);
    }
    registrationLogger.LogInformation("Configuration contract validation succeeded.");
    ConfigurationContractRegistrationResult registration = await store.RegisterAsync(contract, ct);
    registrationLogger.LogInformation("Configuration contract registration succeeded. Changed={Changed}.", registration.Changed);
    return Results.Ok(new { registered = true, registration.ApplicationId, registration.Version, registration.Changed, registration.LastRegisteredAtUtc });
});

app.MapGet("/api/contracts/discovery", async (HttpContext context, IConfigurationContractStore store, IConfiguration configuration, CancellationToken ct) =>
{
    string? configuredKey = configuration["AegisConfiguration:RegistrationKey"];
    if (string.IsNullOrWhiteSpace(configuredKey)) return Results.Problem("Application discovery is disabled because no registration credential is configured.", statusCode: StatusCodes.Status503ServiceUnavailable);
    string supplied = context.Request.Headers["X-Aegis-Registration-Key"].ToString();
    if (!FixedEquals(configuredKey, supplied)) return Results.Unauthorized();
    IReadOnlyList<ApplicationConfigurationContract> contracts = await store.LoadAllAsync(ct);
    context.Response.Headers.CacheControl = "no-store";
    return Results.Ok(contracts);
});

app.MapGet("/api/contracts", async (IConfigurationContractStore store, CancellationToken ct) => Results.Ok(await store.LoadAllAsync(ct)));
app.MapGet("/api/report/{applicationId}", async (string applicationId, IConfigurationContractStore store, ConfigurationEvaluator evaluator, CommissioningReportRenderer renderer, CancellationToken ct) =>
{
    var contract = (await store.LoadAllAsync(ct)).FirstOrDefault(x => string.Equals(x.ApplicationId, applicationId, StringComparison.OrdinalIgnoreCase));
    if (contract is null) return Results.NotFound();
    ConfigurationEvaluation evaluation = await evaluator.EvaluateAsync(contract, ct);
    CommissioningSnapshot snapshot = new(contract.ApplicationId, contract.Version, DateTimeOffset.UtcNow, evaluation);
    return Results.Text(renderer.RenderMarkdown(snapshot), "text/markdown; charset=utf-8");
});
app.MapPost("/api/contracts/validate", (HttpContext context, ApplicationConfigurationContract contract, ContractValidator validator) => Has(context, "Configuration.Manage") ? Results.Ok(validator.Validate(contract)) : Results.Forbid());
app.MapAegisHealth("Aegis.Configuration");
app.Run();

static bool Has(HttpContext c, string permission) => c.Items.TryGetValue("SuiteIdentity", out object? value) && value is SuiteIdentity i && i.Permissions.Contains(permission, StringComparer.OrdinalIgnoreCase);
static IResult Html(string html) => Results.Content(html, "text/html; charset=utf-8");
static string E(string? value) => HtmlEncoder.Default.Encode(value ?? "—");
static bool FixedEquals(string expected, string actual)
{
    byte[] a = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
    byte[] b = SHA256.HashData(Encoding.UTF8.GetBytes(actual));
    return CryptographicOperations.FixedTimeEquals(a, b);
}
static string Registered(DateTimeOffset? value, string format) => value.HasValue ? value.Value.ToString(format) + " UTC" : "Never";

static string RenderDashboard(List<(ApplicationConfigurationContract Contract, ConfigurationEvaluation Evaluation)> rows, int? selectedIndex)
{
    int configured = rows.Count(x => x.Evaluation.IsComplete);
    int requirements = rows.Sum(x => x.Evaluation.Requirements.Count);
    string tableRows = rows.Count == 0 ? "<tr><td colspan='9' class='empty'>No applications registered.</td></tr>" : string.Join("", rows.Select((x, i) => RenderApplicationRow(x, i, selectedIndex == i)));
    int selected = selectedIndex ?? (rows.Count > 0 ? 0 : -1);
    string detail = selected < 0 ? "<section class='detail'><div class='empty'>Applications appear here automatically after they register with System Configuration.</div></section>" : RenderDetails(rows[selected]);
    string activity = rows.Count == 0 ? "<div class='empty'>No registration activity.</div>" : string.Join("", rows.OrderByDescending(x => x.Contract.LastRegisteredAtUtc).Take(4).Select(x => $"<div class='activity-row'><i></i><span><b>{E(x.Contract.DisplayName)}</b><small>Configuration contract registered</small></span><time>{Registered(x.Contract.LastRegisteredAtUtc, "MMM d, HH:mm")}</time></div>"));
    string content = $"<style>{SecretManagerStyles()}</style><section class='page-head'><div><h1>Applications</h1><p>All registered applications and their configuration status</p></div></section><section class='toolbar'><input id='search' placeholder='⌕  Search applications...'><select id='statusFilter'><option value=''>All Status</option><option>Configured</option><option>Not Configured</option></select><button onclick='location.reload()'>↻</button><button class='primary' onclick='document.getElementById(\"registrationInfoDialog\").showModal()'>ⓘ Application Registration</button></section><section class='metrics'><div class='metric'><span>▦</span><b>{rows.Count}</b><small>Total Applications</small></div><div class='metric green'><span>✓</span><b>{configured}</b><small>Configured</small></div><div class='metric amber'><span>!</span><b>{rows.Count - configured}</b><small>Not Configured</small></div><div class='metric'><span>≡</span><b>{requirements}</b><small>Total Requirements</small></div></section><section class='workspace'><div><section class='panel table-panel'><table id='apps'><thead><tr><th>Application</th><th>Version</th><th>Site / Instance</th><th>Configuration</th><th>Secrets</th><th>Required</th><th>Optional</th><th>Last Updated</th><th></th></tr></thead><tbody>{tableRows}</tbody></table></section><section class='lower'><div class='panel'><h3>◴ Recent Activity</h3>{activity}</div><div class='panel'><h3>⌘ Quick Links</h3><div class='quick'><a href='/runbooks'>▤ <span><b>Configuration Runbooks</b><small>Commissioning and recovery guidance</small></span></a><a href='/api/contracts'>◇ <span><b>Contract API</b><small>Machine-readable registered contracts</small></span></a></div></div></section></div>{detail}</section><dialog id='registrationInfoDialog'><h2>Applications Register Automatically</h2><p>Applications publish their configuration requirements to System Configuration when they start. You do not manually add applications here.</p><p><b>Configuration Server</b><br><code id='configurationServerUrl'></code></p><p><b>Registration Endpoint</b><br><code id='registrationEndpointUrl'></code></p><p>Applications should normally configure <code>Aegis:Configuration:Url</code> with the Configuration Server address above.</p><p class='muted'>Registration is authenticated using the application's Configuration Registration Key. Secret values are never displayed here.</p><button onclick='this.closest(\"dialog\").close()'>Close</button></dialog>";
    return ConfigurationShell.Render("Applications", content, "applications");
}

static string RenderApplicationRow((ApplicationConfigurationContract Contract, ConfigurationEvaluation Evaluation) x, int index, bool selected)
{
    SecretManagerStatus secrets = SecretManagerEvaluator.Evaluate(x.Contract, x.Evaluation);
    string appId = Uri.EscapeDataString(x.Contract.ApplicationId);
    string secretSummary = secrets.HasSecrets ? $"<details class='secret-menu' onclick='event.stopPropagation()'><summary class='secret-pill {E(secrets.CssClass)}'><span>{E(secrets.Glyph)}</span>{E(secrets.ProviderName)} · {E(secrets.IssueLabel)}</summary><div class='secret-menu-items'><a href='/applications/{appId}/secrets'>Configure</a><a href='/applications/{appId}/secrets#issues'>View Issues</a>{(secrets.CanOpenProviderConsole ? $"<a href='{E(secrets.ManagementUrl)}' target='_blank' rel='noopener'>Open Provider Console</a>" : string.Empty)}</div></details>" : "<span class='secret-pill neutral'>— No secrets</span>";
    return $"<tr class='{(selected ? "selected" : "")}'><td><a href='/applications/{appId}'><span class='appicon'>{AppGlyph(index)}</span><b>{E(x.Contract.DisplayName)}</b><small>{E(x.Contract.ApplicationId)}</small></a></td><td>{E(x.Contract.Version)}</td><td>{E(x.Contract.SiteId)}<small>{E(x.Contract.InstanceId)}</small></td><td><span class='status {(x.Evaluation.IsComplete ? "good" : "warn")}'>{(x.Evaluation.IsComplete ? "Configured" : "Not Configured")}</span></td><td>{secretSummary}</td><td>{x.Evaluation.Requirements.Count(r => r.Required)}</td><td>{x.Evaluation.Requirements.Count(r => !r.Required)}</td><td>{Registered(x.Contract.LastRegisteredAtUtc, "MMM d, HH:mm")}</td><td>›</td></tr>";
}

static string RenderDetails((ApplicationConfigurationContract Contract, ConfigurationEvaluation Evaluation) x)
{
    string req = string.Join("", x.Evaluation.Requirements.Where(r => r.Required).Select(RenderRequirement));
    string opt = string.Join("", x.Evaluation.Requirements.Where(r => !r.Required).Select(RenderRequirement));
    string environment = RenderRuntimeEnvironment(x.Contract.RuntimeEnvironment);
    string appId = Uri.EscapeDataString(x.Contract.ApplicationId);
    SecretManagerStatus secrets = SecretManagerEvaluator.Evaluate(x.Contract, x.Evaluation);
    string secretsSummary = secrets.HasSecrets ? $"<a class='secret-detail-link' href='/applications/{appId}/secrets'><span>{E(secrets.Glyph)}</span><span><b>Secrets: {E(secrets.ProviderName)}</b><small>{E(secrets.IssueLabel)}</small></span></a>" : "<div class='secret-detail-link disabled'><span>—</span><span><b>Secrets</b><small>No secret requirements declared</small></span></div>";
    return $"<aside class='detail'><div class='detail-head'><span class='bigicon'>▣</span><div><h2>{E(x.Contract.DisplayName)}</h2><small>{E(x.Contract.ApplicationId)}</small></div><span class='status {(x.Evaluation.IsComplete ? "good" : "warn")}'>{(x.Evaluation.IsComplete ? "Configured" : "Not Configured")}</span></div><div class='facts'><div><small>VERSION</small><b>{E(x.Contract.Version)}</b></div><div><small>SITE / INSTANCE</small><b>{E(x.Contract.SiteId)} / {E(x.Contract.InstanceId)}</b></div><div><small>REGISTERED</small><b>{Registered(x.Contract.LastRegisteredAtUtc, "MMM d, yyyy")}</b></div><div><small>APPLICATION ID</small><b>{E(x.Contract.ApplicationId)}</b></div></div>{secretsSummary}<nav class='tabs'><b>Requirements</b><a href='/applications/{appId}/details'>Details</a><a href='/applications/{appId}/details#diagnostics'>Diagnostics</a><a href='/runbooks/{appId}'>Runbooks</a><a href='/applications/{appId}/contract'>Contract</a></nav><h3>Required Requirements</h3><div class='reqs'>{(string.IsNullOrEmpty(req) ? "<div class='empty'>None</div>" : req)}</div><h3>Optional Requirements</h3><div class='reqs'>{(string.IsNullOrEmpty(opt) ? "<div class='empty'>None</div>" : opt)}</div>{environment}</aside>";
}

static string RenderRuntimeEnvironment(RuntimeEnvironmentSnapshot? environment)
{
    if (environment is null) return "<h3>Detected Runtime Environment</h3><div class='empty'>No runtime environment snapshot has been published.</div>";
    string interfaces = environment.NetworkInterfaces is not { Count: > 0 } ? "<div class='empty'>No non-loopback interfaces reported.</div>" : string.Join("", environment.NetworkInterfaces.Select(item => $"<div class='req'><span><b>{E(item.Name)}</b><small>{E(item.Type)} · Gateways: {E(item.Gateways is { Count: > 0 } ? string.Join(", ", item.Gateways) : null)}</small></span><code>{E(item.Addresses is { Count: > 0 } ? string.Join(", ", item.Addresses) : null)}</code><i class='ok'>N</i></div>"));
    string storage = environment.Storage is not { Count: > 0 } ? "<div class='empty'>No storage identity reported.</div>" : string.Join("", environment.Storage.Select(volume => $"<div class='req'><span><b>{E(volume.Name)}</b><small>{E(volume.DriveType)} · {E(volume.Format)}</small></span><code>Deployment identity</code><i class='ok'>D</i></div>"));
    return $"<h3>Detected Runtime Environment</h3><div class='facts'><div><small>MACHINE / HOST</small><b>{E(environment.MachineName)} / {E(environment.HostName)}</b></div><div><small>HOSTING ENVIRONMENT</small><b>{E(environment.HostingEnvironment)}</b></div><div><small>OPERATING SYSTEM</small><b>{E(environment.OperatingSystem)}</b></div><div><small>ARCHITECTURE</small><b>{E(environment.OsArchitecture)} / {E(environment.ProcessArchitecture)}</b></div><div><small>.NET</small><b>{E(environment.FrameworkDescription)}</b></div><div><small>PROCESSORS</small><b>{environment.ProcessorCount?.ToString() ?? "—"}</b></div><div><small>DNS DOMAIN</small><b>{E(environment.DnsDomainName)}</b></div></div><h3>Detected Network Identity</h3><div class='reqs'>{interfaces}</div><h3>Detected Storage Identity</h3><div class='reqs'>{storage}</div>";
}

static string RenderRequirement(RequirementEvaluation r)
{
    string value = r.Sensitive ? "Protected / not displayed" : E(r.SafeDisplayValue);
    string source = string.IsNullOrWhiteSpace(r.EffectiveSource) ? "Source: unresolved" : $"Source: {E(r.EffectiveSource)}";
    string defaultText = r.HasDefault && !r.Sensitive ? $" · Default: {E(r.DefaultValue)}" : string.Empty;
    string condition = string.IsNullOrWhiteSpace(r.ConditionalOn) ? string.Empty : $" · When: {E(r.ConditionalOn)}";
    string stateLabel = r.State switch { ConfigurationState.Configured => "Configured", ConfigurationState.Default => "Default", ConfigurationState.Unknown => "Unknown", ConfigurationState.Missing => "Missing", _ => "Optional" };
    string iconClass = r.State is ConfigurationState.Configured or ConfigurationState.Default ? "ok" : "bad";
    string icon = r.State switch { ConfigurationState.Configured => "✓", ConfigurationState.Default => "D", ConfigurationState.Unknown => "?", _ => "!" };
    return $"<div class='req'><span><b>{E(r.DisplayName)}</b><small>{E(r.Purpose ?? r.Message)}</small><small>{stateLabel} · {source}{defaultText}{condition}</small></span><code>{value}</code><i class='{iconClass}'>{icon}</i></div>";
}
static string AppGlyph(int i) => new[] { "▣", "◇", "▦", "⌁", "◈", "▤" }[i % 6];
static string SecretManagerStyles() => ".secret-pill{display:inline-flex;align-items:center;gap:5px;border:1px solid;border-radius:5px;padding:5px 8px;font-weight:700;white-space:nowrap}.secret-pill.good{background:#00663f;border-color:#0c9e67}.secret-pill.warn{background:#6d4911;border-color:#c88a27}.secret-pill.neutral{background:#24394a;border-color:#5c7283}.secret-menu{position:relative}.secret-menu summary{cursor:pointer;list-style:none}.secret-menu summary::-webkit-details-marker{display:none}.secret-menu-items{position:absolute;z-index:20;right:0;min-width:190px;background:#001827;border:1px solid #2c7bab;border-radius:6px;padding:6px;box-shadow:0 8px 22px #0008}.secret-menu-items a{display:block;padding:8px;border-radius:4px;color:#ddecf7}.secret-menu-items a:hover{background:#07314a}.secret-detail-link{display:flex;align-items:center;gap:10px;margin:12px 0;padding:10px;border:1px solid #2c7bab;border-radius:6px;background:#062238;color:#e8f5ff}.secret-detail-link>span:first-child{font-size:20px}.secret-detail-link small{display:block;color:#9fb7c8}.secret-detail-link.disabled{opacity:.75}";