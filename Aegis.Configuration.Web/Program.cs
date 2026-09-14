using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json.Serialization;
using Aegis.Configuration.Application;
using Aegis.Configuration.Domain;
using Aegis.Configuration.Infrastructure;
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
app.MapGet("/runbooks", async (IConfigurationContractStore store, CancellationToken ct) =>
{
    var contracts = await store.LoadAllAsync(ct);
    string cards = contracts.Count == 0 ? "<div class='empty'>No runbooks available.</div>" : string.Join("", contracts.OrderBy(x => x.DisplayName).Select(x => $"<a class='runbook-card' href='/runbooks/{Uri.EscapeDataString(x.ApplicationId)}'><span class='appicon'>▤</span><span><b>{E(x.DisplayName)}</b><small>Version {E(x.Version)}</small></span><span>›</span></a>"));
    return Html(ConfigurationShell.Render("Runbooks", $"<section class='page-head'><div><h1>Runbooks</h1><p>Configuration commissioning and recovery instructions</p></div></section><section class='panel'><h2>Configuration Runbooks</h2>{cards}</section>", "runbooks"));
});
app.MapGet("/runbooks/{applicationId}", async (string applicationId, IConfigurationContractStore store, CancellationToken ct) =>
{
    var contract = (await store.LoadAllAsync(ct)).FirstOrDefault(x => string.Equals(x.ApplicationId, applicationId, StringComparison.OrdinalIgnoreCase));
    if (contract is null) return Results.NotFound();
    return Html(ConfigurationShell.Render("Runbook", $"<section class='page-head'><div><h1>{E(contract.DisplayName)} Runbook</h1><p>Commissioning guidance for {E(contract.ApplicationId)}</p></div></section><section class='panel'><h2>Configuration Contract</h2><p>Version {E(contract.Version)} · Site {E(contract.SiteId)} · Instance {E(contract.InstanceId)}</p><a class='primary' href='/applications/{Uri.EscapeDataString(contract.ApplicationId)}'>Back to application</a></section>", "runbooks"));
});
app.MapPost("/api/contracts/register", async (HttpContext context, ApplicationConfigurationContract contract, IConfigurationContractRegistrationStore store, ContractValidator validator, IConfiguration configuration, CancellationToken ct) =>
{
    if (!Has(context, "Configuration.Manage")) return Results.Forbid();
    var validation = validator.Validate(contract);
    if (!validation.IsValid) return Results.BadRequest(validation);
    string? configuredKey = configuration["AegisConfiguration:RegistrationKey"];
    if (!string.IsNullOrWhiteSpace(configuredKey))
    {
        string supplied = context.Request.Headers["X-Aegis-Registration-Key"].ToString();
        if (!FixedEquals(configuredKey, supplied)) return Results.Unauthorized();
    }
    await store.SaveAsync(contract, ct);
    return Results.Ok(new { registered = true, contract.ApplicationId });
});
app.MapGet("/api/contracts", async (IConfigurationContractStore store, CancellationToken ct) => Results.Ok(await store.LoadAllAsync(ct)));
app.MapGet("/api/report/{applicationId}", async (string applicationId, IConfigurationContractStore store, ConfigurationEvaluator evaluator, CommissioningReportRenderer renderer, CancellationToken ct) =>
{
    var contract = (await store.LoadAllAsync(ct)).FirstOrDefault(x => string.Equals(x.ApplicationId, applicationId, StringComparison.OrdinalIgnoreCase));
    if (contract is null) return Results.NotFound();
    return Results.Text(renderer.Render(await evaluator.EvaluateAsync(contract, ct)), "text/plain");
});
app.MapPost("/api/contracts/validate", (HttpContext context, ApplicationConfigurationContract contract, ContractValidator validator) => Has(context, "Configuration.Manage") ? Results.Ok(validator.Validate(contract)) : Results.Forbid());
app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "Aegis.Configuration", displayName = "System Configuration" }));
app.Run();

static bool Has(HttpContext c, string permission) => c.Items.TryGetValue("SuiteIdentity", out object? value) && value is SuiteIdentity i && i.Permissions.Contains(permission, StringComparer.OrdinalIgnoreCase);
static IResult Html(string html) => Results.Content(html, "text/html; charset=utf-8");
static string E(string? value) => HtmlEncoder.Default.Encode(value ?? "—");
static bool FixedEquals(string expected, string actual){byte[] a=SHA256.HashData(Encoding.UTF8.GetBytes(expected));byte[] b=SHA256.HashData(Encoding.UTF8.GetBytes(actual));return CryptographicOperations.FixedTimeEquals(a,b);}

static string RenderDashboard(List<(ApplicationConfigurationContract Contract, ConfigurationEvaluation Evaluation)> rows, int? selectedIndex)
{
    int configured = rows.Count(x => x.Evaluation.IsConfigured); int requirements = rows.Sum(x => x.Evaluation.Requirements.Count);
    string tableRows = rows.Count == 0 ? "<tr><td colspan='8' class='empty'>No applications registered.</td></tr>" : string.Join("", rows.Select((x,i) => $"<tr class='{(selectedIndex==i?"selected":"")}'><td><a href='/applications/{Uri.EscapeDataString(x.Contract.ApplicationId)}'><span class='appicon'>{AppGlyph(i)}</span><b>{E(x.Contract.DisplayName)}</b><small>{E(x.Contract.ApplicationId)}</small></a></td><td>{E(x.Contract.Version)}</td><td>{E(x.Contract.SiteId)}<small>{E(x.Contract.InstanceId)}</small></td><td><span class='status {(x.Evaluation.IsConfigured?"good":"warn")}'>{(x.Evaluation.IsConfigured?"Configured":"Not Configured")}</span></td><td>{x.Evaluation.Requirements.Count(r=>r.Required)}</td><td>{x.Evaluation.Requirements.Count(r=>!r.Required)}</td><td>{x.Contract.LastRegisteredAtUtc.ToLocalTime():MMM d, HH:mm}</td><td>›</td></tr>"));
    int selected = selectedIndex ?? (rows.Count>0?0:-1);
    string detail = selected < 0 ? "<section class='detail'><div class='empty'>Select or register an application to inspect its configuration.</div></section>" : RenderDetails(rows[selected]);
    string activity = rows.Count == 0 ? "<div class='empty'>No registration activity.</div>" : string.Join("", rows.OrderByDescending(x=>x.Contract.LastRegisteredAtUtc).Take(4).Select(x=>$"<div class='activity-row'><i></i><span><b>{E(x.Contract.DisplayName)}</b><small>Configuration contract registered</small></span><time>{x.Contract.LastRegisteredAtUtc.ToLocalTime():MMM d, HH:mm}</time></div>"));
    string content = $"<section class='page-head'><div><h1>Applications</h1><p>All registered applications and their configuration status</p></div></section><section class='toolbar'><input id='search' placeholder='⌕  Search applications...'><select id='statusFilter'><option value=''>All Status</option><option>Configured</option><option>Not Configured</option></select><button onclick='location.reload()'>↻</button><button class='primary' onclick='document.getElementById(\"registerDialog\").showModal()'>＋ Register Application</button></section><section class='metrics'><div class='metric'><span>▦</span><b>{rows.Count}</b><small>Total Applications</small></div><div class='metric green'><span>✓</span><b>{configured}</b><small>Configured</small></div><div class='metric amber'><span>!</span><b>{rows.Count-configured}</b><small>Not Configured</small></div><div class='metric'><span>≡</span><b>{requirements}</b><small>Total Requirements</small></div></section><section class='workspace'><div><section class='panel table-panel'><table id='apps'><thead><tr><th>Application</th><th>Version</th><th>Site / Instance</th><th>Status</th><th>Required</th><th>Optional</th><th>Last Updated</th><th></th></tr></thead><tbody>{tableRows}</tbody></table></section><section class='lower'><div class='panel'><h3>◴ Recent Activity</h3>{activity}</div><div class='panel'><h3>⌘ Quick Links</h3><div class='quick'><a href='/runbooks'>▤ <span><b>Configuration Runbooks</b><small>Commissioning and recovery guidance</small></span></a><a href='/api/contracts'>◇ <span><b>Contract API</b><small>Registered application contracts</small></span></a></div></div></section></div>{detail}</section><dialog id='registerDialog'><h2>Register Application</h2><p>Applications register through the authenticated contract registration API.</p><code>POST /api/contracts/register</code><p class='muted'>Use the application registration key. Sensitive values are never entered here.</p><button onclick='this.closest(\"dialog\").close()'>Close</button></dialog>";
    return ConfigurationShell.Render("Applications", content, "applications");
}
static string RenderDetails((ApplicationConfigurationContract Contract, ConfigurationEvaluation Evaluation) x){string req=string.Join("",x.Evaluation.Requirements.Where(r=>r.Required).Select(RenderRequirement));string opt=string.Join("",x.Evaluation.Requirements.Where(r=>!r.Required).Select(RenderRequirement));return $"<aside class='detail'><div class='detail-head'><span class='bigicon'>▣</span><div><h2>{E(x.Contract.DisplayName)}</h2><small>{E(x.Contract.ApplicationId)}</small></div><span class='status {(x.Evaluation.IsConfigured?"good":"warn")}'>{(x.Evaluation.IsConfigured?"Configured":"Not Configured")}</span></div><div class='facts'><div><small>VERSION</small><b>{E(x.Contract.Version)}</b></div><div><small>SITE / INSTANCE</small><b>{E(x.Contract.SiteId)} / {E(x.Contract.InstanceId)}</b></div><div><small>REGISTERED</small><b>{x.Contract.LastRegisteredAtUtc.ToLocalTime():MMM d, yyyy}</b></div><div><small>APPLICATION ID</small><b>{E(x.Contract.ApplicationId)}</b></div></div><nav class='tabs'><b>Requirements</b><a href='/api/report/{Uri.EscapeDataString(x.Contract.ApplicationId)}'>Details</a><a href='/runbooks/{Uri.EscapeDataString(x.Contract.ApplicationId)}'>Runbooks</a><a href='/api/contracts'>Contract</a></nav><h3>Required Requirements</h3><div class='reqs'>{(string.IsNullOrEmpty(req)?"<div class='empty'>None</div>":req)}</div><h3>Optional Requirements</h3><div class='reqs'>{(string.IsNullOrEmpty(opt)?"<div class='empty'>None</div>":opt)}</div><div class='actions'><a href='/api/contracts'>View Contract</a><a href='/runbooks/{Uri.EscapeDataString(x.Contract.ApplicationId)}'>View Runbooks</a><a href='/api/report/{Uri.EscapeDataString(x.Contract.ApplicationId)}'>Print / PDF</a></div></aside>";}
static string RenderRequirement(ConfigurationRequirementEvaluation r)=>$"<div class='req'><span><b>{E(r.Name)}</b><small>{E(r.Description)}</small></span><code>{(r.Sensitive?"Protected / not displayed":E(r.DisplayValue))}</code><i class='{(r.IsSatisfied?"ok":"bad")}'>{(r.IsSatisfied?"✓":"!")}</i></div>";
static string AppGlyph(int i)=>new[]{"▣","◇","▦","⌁","◈","▤"}[i%6];
