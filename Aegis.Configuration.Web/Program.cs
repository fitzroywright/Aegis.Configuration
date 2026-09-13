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
DirectoryContractStore contractStore = new(contractDirectory);
builder.Services.AddSingleton<IConfigurationContractStore>(contractStore);
builder.Services.AddSingleton<IConfigurationContractRegistrationStore>(contractStore);
builder.Services.AddSingleton(new InstructionPackCatalog(instructionDirectory));
builder.Services.AddSingleton<ConfigurationEvaluator>();
builder.Services.AddSingleton<CommissioningService>();
builder.Services.AddSingleton<CommissioningReportRenderer>();
builder.Services.AddSingleton<FleetStatusCache>();
builder.Services.AddSingleton<ContractValidator>();
WebApplication app = builder.Build();

app.MapGet("/", async (IConfigurationContractStore store, ConfigurationEvaluator evaluator, CancellationToken ct) =>
{
    IReadOnlyList<ApplicationConfigurationContract> contracts = await store.LoadAllAsync(ct);
    List<(ApplicationConfigurationContract Contract, ConfigurationEvaluation Evaluation)> rows = [];
    foreach (ApplicationConfigurationContract contract in contracts) rows.Add((contract, await evaluator.EvaluateAsync(contract, ct)));
    string cards = rows.Count == 0 ? "<section class='panel empty'><h2>NO APPLICATIONS REGISTERED</h2><p>Start an Aegis application to publish its configuration contract.</p></section>" : string.Join("", rows.Select(x => RenderApplication(x.Contract, x.Evaluation)));
    return Html(Page("Applications", "Applications › Configuration Registry", cards));
});

app.MapGet("/applications/{applicationId}", async (string applicationId, IConfigurationContractStore store, ConfigurationEvaluator evaluator, CancellationToken ct) =>
{
    ApplicationConfigurationContract? contract = (await store.LoadAllAsync(ct)).FirstOrDefault(x => string.Equals(x.ApplicationId, applicationId, StringComparison.OrdinalIgnoreCase));
    if (contract is null) return Results.NotFound();
    ConfigurationEvaluation evaluation = await evaluator.EvaluateAsync(contract, ct);
    return Html(Page(contract.DisplayName, $"Applications › {E(contract.DisplayName)}", RenderApplication(contract, evaluation, detailsOnly: true)));
});

app.MapGet("/runbooks", async (IConfigurationContractStore store, CancellationToken ct) =>
{
    IReadOnlyList<ApplicationConfigurationContract> contracts = await store.LoadAllAsync(ct);
    string body = contracts.Count == 0
        ? "<section class='panel empty'><h2>NO RUNBOOKS AVAILABLE</h2><p>Runbooks appear after applications register their configuration requirements.</p></section>"
        : "<section class='panel'><div class='sectiontitle'>Configuration Runbooks</div><div class='actions' style='margin:10px'>" + string.Join("", contracts.OrderBy(x => x.DisplayName).Select(x => $"<a class='btn' href='/runbooks/{Uri.EscapeDataString(x.ApplicationId)}'>{E(x.DisplayName)} · {E(x.Version)}</a>")) + "</div></section>";
    return Html(Page("Runbooks", "Runbooks", body));
});

app.MapGet("/runbooks/{applicationId}", async (string applicationId, CommissioningService service, InstructionPackCatalog packs, CommissioningReportRenderer renderer, CancellationToken ct) =>
{
    CommissioningSnapshot? snapshot = (await service.EvaluateAllAsync(ct)).FirstOrDefault(x => string.Equals(x.ApplicationId, applicationId, StringComparison.OrdinalIgnoreCase));
    if (snapshot is null) return Results.NotFound();
    string markdown = renderer.RenderMarkdown(snapshot, await packs.FindAsync(snapshot.ApplicationId, snapshot.ApplicationVersion, ct));
    string body = $"<section class='panel runbook'><div class='runhead'><div><h2>{E(snapshot.ApplicationId)} Configuration Runbook</h2><div class='muted'>Version {E(snapshot.ApplicationVersion)}</div></div><button class='btn primary' onclick='window.print()'>Print / Save PDF</button></div><pre>{E(markdown)}</pre></section>";
    return Html(Page("Runbook", $"Applications › {E(snapshot.ApplicationId)} › Runbook", body));
});

app.MapPost("/api/contracts/register", async (ApplicationConfigurationContract contract, HttpContext context, IConfigurationContractRegistrationStore store, ContractValidator validator, ISecretProvider secrets, FleetStatusCache cache, CancellationToken ct) =>
{
    ContractValidationResult validation = validator.Validate(contract);
    if (!validation.IsValid) return Results.BadRequest(validation);
    if (!DirectoryContractStore.IsSafeApplicationId(contract.ApplicationId)) return Results.BadRequest(new { error = "ApplicationId may contain only letters, digits, '.', '-' and '_'." });
    string providedKey = context.Request.Headers["X-Configuration-Registration-Key"].FirstOrDefault() ?? string.Empty;
    string? expectedKey = await secrets.GetAsync($"configuration/registration/{contract.ApplicationId}", ct);
    if (string.IsNullOrWhiteSpace(expectedKey) || !FixedTimeEquals(providedKey, expectedKey)) return Results.Unauthorized();
    try { ConfigurationContractRegistrationResult result = await store.RegisterAsync(contract, ct); if (result.Changed) cache.Invalidate(); return Results.Ok(result); }
    catch (InvalidDataException ex) { return Results.BadRequest(new { error = ex.Message }); }
});
app.MapGet("/api/contracts", async (IConfigurationContractStore store, CancellationToken ct) => Results.Ok(await store.LoadAllAsync(ct)));
app.MapGet("/api/report/{applicationId}", async (string applicationId, CommissioningService service, InstructionPackCatalog packs, CommissioningReportRenderer renderer, CancellationToken ct) => { CommissioningSnapshot? snapshot = (await service.EvaluateAllAsync(ct)).FirstOrDefault(x => string.Equals(x.ApplicationId, applicationId, StringComparison.OrdinalIgnoreCase)); if (snapshot is null) return Results.NotFound(); return Results.Text(renderer.RenderMarkdown(snapshot, await packs.FindAsync(snapshot.ApplicationId, snapshot.ApplicationVersion, ct)), "text/markdown"); });
app.MapPost("/api/contracts/validate", (ApplicationConfigurationContract contract, ContractValidator validator) => Results.Ok(validator.Validate(contract)));
app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "Aegis.Configuration" }));
app.Run();

static IResult Html(string value) => Results.Content(value, "text/html");
static string E(string? value) => HtmlEncoder.Default.Encode(value ?? "");
static bool FixedTimeEquals(string left, string right) { byte[] a = Encoding.UTF8.GetBytes(left); byte[] b = Encoding.UTF8.GetBytes(right); return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b); }

static string Page(string title, string crumb, string content) => $$"""
<!doctype html><html><head><title>{{E(title)}} · Aegis Configuration</title><meta name="viewport" content="width=device-width,initial-scale=1,viewport-fit=cover"><style>
:root{--bg:#02070d;--panel:#071421;--panel2:#0b1c2b;--line:#27547d;--ink:#eaf3ff;--muted:#9db7d4;--orange:#ff9b2f;--blue:#7896ff;--green:#50e59a;--yellow:#ffd05c;--red:#ff7777}*{box-sizing:border-box}body{margin:0;background:var(--bg);color:var(--ink);font:14px/1.35 Arial,Helvetica,sans-serif}a{color:inherit;text-decoration:none}.shell{min-height:100vh;display:grid;grid-template-columns:92px 1fr}.rail{padding:10px 6px}.brandcap{height:52px;background:var(--blue);border-radius:28px 0 0 28px;margin-bottom:8px}.nav{display:grid;gap:5px}.nav a{min-height:70px;display:grid;place-items:center;text-align:center;background:var(--blue);color:#06101a;font-weight:800;border-radius:20px 0 0 20px;padding:6px}.nav a.active{background:var(--orange)}.main{padding:10px 14px 28px;min-width:0}.mast{border-bottom:4px solid var(--orange);padding:2px 0 8px}.title{font-size:clamp(24px,3vw,42px);font-weight:900}.title b,.sectiontitle{color:var(--orange)}.tag,.muted{color:var(--muted)}.tag{font-size:12px}.crumb{margin:8px 0;padding:7px 12px;border:1px solid var(--line);border-radius:16px;background:var(--panel)}.panel{border:2px solid var(--line);border-radius:14px;background:linear-gradient(180deg,var(--panel2),var(--panel));margin:9px 0;overflow:hidden}.appgrid{display:grid;grid-template-columns:minmax(240px,.9fr) minmax(210px,.75fr) minmax(210px,.65fr);gap:8px;padding:10px}.identity,.status,.actions{border:1px solid var(--line);border-radius:9px;padding:11px}.identity h2,.runhead h2{margin:0 0 2px;font-size:20px}.meta{display:grid;grid-template-columns:105px 1fr;gap:3px;margin-top:9px}.complete{color:var(--green);font-size:22px;font-weight:900}.incomplete{color:var(--yellow);font-size:22px;font-weight:900}.actions{display:grid;gap:6px}.btn{display:block;border:1px solid var(--blue);border-radius:8px;padding:10px 12px;text-align:center;background:transparent;color:var(--ink);font:inherit;cursor:pointer}.btn.primary{background:#0d55d8}.sectiontitle{padding:7px 12px;font-weight:900;border-top:1px solid var(--line)}.table{display:grid}.tr{display:grid;grid-template-columns:1fr 1.6fr .55fr 1.25fr .8fr 1.25fr;border-top:1px solid #24425e}.tr>div{padding:8px 10px;min-width:0;overflow-wrap:anywhere}.thead{background:#122a3d;font-weight:800}.badge{display:inline-block;padding:3px 8px;border-radius:12px;font-weight:800}.required{background:var(--red);color:#101010}.optional{background:var(--blue);color:#06101a}.ok{color:var(--green)}.missing{color:var(--red)}.footer{margin-top:12px;border-top:3px solid var(--orange);padding:8px;color:var(--muted);display:flex;gap:18px;flex-wrap:wrap}.curveback{position:fixed;left:5px;top:5px;width:84px;height:48px;border:0;border-radius:28px 0 0 28px;background:transparent;cursor:pointer;z-index:3}.empty{padding:24px}.runbook{padding:14px}.runhead{display:flex;justify-content:space-between;gap:12px;align-items:center;margin-bottom:12px}.runbook pre{white-space:pre-wrap;overflow-wrap:anywhere;background:#030a11;border:1px solid var(--line);border-radius:9px;padding:16px;color:var(--ink);font:13px/1.5 ui-monospace,Consolas,monospace}
@media(max-width:900px){.shell{grid-template-columns:72px 1fr}.nav a{min-height:62px;font-size:11px}.appgrid{grid-template-columns:1fr 1fr}.actions{grid-column:1/-1;grid-template-columns:repeat(3,1fr)}.tr{grid-template-columns:1fr .7fr 1.2fr .8fr}.tr .desc,.tr .example{display:none}.title{font-size:27px}}
@media(max-width:600px){body{font-size:13px}.shell{display:block}.rail{height:58px;padding:5px 8px;display:flex;align-items:center;border-bottom:3px solid var(--orange);position:sticky;top:0;z-index:2}.brandcap{width:42px;height:38px;margin:0 8px 0 0;border-radius:20px 0 0 20px}.nav{display:none}.main{padding:8px 8px 74px}.mast{border:0;padding:0 4px 5px}.title{font-size:22px}.tag{display:none}.crumb{margin:5px 0}.appgrid{grid-template-columns:1fr;padding:7px}.actions{grid-column:auto;grid-template-columns:1fr}.identity,.status,.actions{padding:10px}.meta{grid-template-columns:90px 1fr}.table{gap:6px;padding:7px}.thead{display:none}.tr{display:block;border:1px solid var(--line);border-radius:9px;background:#071522;padding:8px}.tr>div{padding:2px 4px}.tr .desc,.tr .example{display:none}.tr .name{font-weight:900;font-size:14px}.tr .reqflag{float:right}.tr .value{color:var(--muted);font-size:12px}.footer{position:fixed;bottom:0;left:0;right:0;margin:0;background:#06111c;border-top:2px solid var(--orange);justify-content:space-around;padding:12px 6px;z-index:3}.footer a{font-size:11px}.curveback{width:48px;height:42px}.runhead{display:block}.runhead .btn{margin-top:10px}}
@media print{body{background:#fff;color:#000}.rail,.mast,.crumb,.footer,.curveback,.runhead .btn{display:none!important}.shell{display:block}.main{padding:0}.panel,.runbook pre{border:0;background:#fff;color:#000}.runbook{margin:0;padding:0}}
</style></head><body><button class="curveback" aria-label="Back" onclick="history.back()"></button><div class="shell"><aside class="rail"><div class="brandcap"></div><nav class="nav"><a href="/">DASHBOARD</a><a class="active" href="/">APPLICATIONS</a><a href="/runbooks">RUNBOOKS</a></nav></aside><main class="main"><header class="mast"><div class="title"><b>AEGIS</b> CONFIGURATION</div><div class="tag">KNOW WHAT IS REQUIRED. KNOW WHAT IS CONFIGURED.</div></header><div class="crumb">⌂ &nbsp; {{crumb}}</div>{{content}}<footer class="footer"><a href="/">Configuration</a><a href="/api/contracts">Contracts</a><span>A Safer Tomorrow</span></footer></main></div></body></html>
""";

static string RenderApplication(ApplicationConfigurationContract contract, ConfigurationEvaluation evaluation, bool detailsOnly = false)
{
    int required = contract.Requirements.Count(x => x.Required);
    int requiredConfigured = evaluation.Requirements.Count(x => x.Required && x.State == ConfigurationState.Configured);
    int optional = contract.Requirements.Count(x => !x.Required);
    int optionalConfigured = evaluation.Requirements.Count(x => !x.Required && x.State == ConfigurationState.Configured);
    bool complete = evaluation.IsComplete;
    string id = Uri.EscapeDataString(contract.ApplicationId);
    string requirements = string.Join("", evaluation.Requirements.Select(r => $"<div class='tr'><div class='name'>{E(r.DisplayName)}</div><div class='desc'>{E(r.Purpose)}</div><div class='reqflag'><span class='badge {(r.Required ? "required" : "optional")}'>{(r.Required ? "Required" : "Optional")}</span></div><div class='value'>{E(r.Sensitive ? "Protected / not displayed" : r.SafeDisplayValue ?? (r.State == ConfigurationState.Configured ? "Configured" : "Not configured"))}</div><div class='{(r.State == ConfigurationState.Missing ? "missing" : "ok")}'>{(r.State == ConfigurationState.Missing ? "● Missing" : r.State == ConfigurationState.OptionalNotConfigured ? "○ Optional" : "● Configured")}</div><div class='example'>{E(r.Example)}</div></div>"));
    string actions = detailsOnly ? $"<a class='btn primary' href='/runbooks/{id}'>View Runbook</a><a class='btn' href='/'>Back to Applications</a>" : $"<a class='btn primary' href='/runbooks/{id}'>View Runbook</a><a class='btn' href='/runbooks/{id}'>Export / Print Runbook</a><a class='btn' href='/applications/{id}'>Application Details</a>";
    return $"<section class='panel'><div class='appgrid'><div class='identity'><h2>{E(contract.DisplayName)}</h2><div class='muted'>{E(contract.Description ?? "Registered Aegis application")}</div><div class='meta'><b>Application ID</b><span>{E(contract.ApplicationId)}</span><b>Version</b><span>{E(contract.Version)}</span><b>Instance</b><span>{E(contract.InstanceId ?? "—")}</span><b>Site</b><span>{E(contract.SiteId ?? "—")}</span></div></div><div class='status'><div class='{(complete ? "complete" : "incomplete")}'>{(complete ? "✓ Complete" : "● Incomplete")}</div><p>{requiredConfigured}/{required} required &nbsp; {optionalConfigured}/{optional} optional</p><div class='muted'>{(complete ? "All required configuration values are supplied." : "One or more required configuration values are missing.")}</div></div><div class='actions'>{actions}</div></div><div class='sectiontitle'>Configuration Requirements</div><div class='table'><div class='tr thead'><div>Setting Name</div><div class='desc'>Description</div><div>Requirement</div><div>Current Value</div><div>Status</div><div class='example'>Example / Notes</div></div>{requirements}</div></section>";
}
