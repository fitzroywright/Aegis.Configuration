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
builder.Services.AddSingleton<ContractValidator>();
WebApplication app = builder.Build();

app.MapGet("/", async (IConfigurationContractStore store, ConfigurationEvaluator evaluator, CancellationToken ct) =>
{
    IReadOnlyList<ApplicationConfigurationContract> contracts = await store.LoadAllAsync(ct);
    List<(ApplicationConfigurationContract Contract, ConfigurationEvaluation Evaluation)> rows = [];
    foreach (ApplicationConfigurationContract contract in contracts.OrderBy(x => x.DisplayName)) rows.Add((contract, await evaluator.EvaluateAsync(contract, ct)));
    string cards = rows.Count == 0 ? "<section class='panel empty'><h2>NO APPLICATIONS REGISTERED</h2><p>Start an Aegis application to publish its configuration contract.</p></section>" : string.Join("", rows.Select(x => RenderApplication(x.Contract, x.Evaluation)));
    return Html(Page("Applications", "Applications › Configuration Registry", cards, "applications"));
});

app.MapGet("/applications/{applicationId}", async (string applicationId, IConfigurationContractStore store, ConfigurationEvaluator evaluator, CancellationToken ct) =>
{
    ApplicationConfigurationContract? contract = (await store.LoadAllAsync(ct)).FirstOrDefault(x => string.Equals(x.ApplicationId, applicationId, StringComparison.OrdinalIgnoreCase));
    if (contract is null) return Results.NotFound();
    ConfigurationEvaluation evaluation = await evaluator.EvaluateAsync(contract, ct);
    return Html(Page(contract.DisplayName, $"Applications › {E(contract.DisplayName)}", RenderApplication(contract, evaluation, true), "applications"));
});

app.MapGet("/runbooks", async (IConfigurationContractStore store, CancellationToken ct) =>
{
    IReadOnlyList<ApplicationConfigurationContract> contracts = await store.LoadAllAsync(ct);
    string body = contracts.Count == 0 ? "<section class='panel empty'><h2>NO RUNBOOKS AVAILABLE</h2><p>Runbooks appear after applications register their configuration requirements.</p></section>" : "<section class='panel'><div class='sectiontitle'>Configuration Runbooks</div><div class='runbooklist'>" + string.Join("", contracts.OrderBy(x => x.DisplayName).Select(x => $"<a class='btn' href='/runbooks/{Uri.EscapeDataString(x.ApplicationId)}'>{E(x.DisplayName)} <span class='muted'>· {E(x.Version)}</span></a>")) + "</div></section>";
    return Html(Page("Runbooks", "Runbooks", body, "runbooks"));
});

app.MapGet("/runbooks/{applicationId}", async (string applicationId, CommissioningService service, InstructionPackCatalog packs, CommissioningReportRenderer renderer, CancellationToken ct) =>
{
    CommissioningSnapshot? snapshot = (await service.EvaluateAllAsync(ct)).FirstOrDefault(x => string.Equals(x.ApplicationId, applicationId, StringComparison.OrdinalIgnoreCase));
    if (snapshot is null) return Results.NotFound();
    string markdown = renderer.RenderMarkdown(snapshot, await packs.FindAsync(snapshot.ApplicationId, snapshot.ApplicationVersion, ct));
    string body = $"<section class='panel runbook'><div class='runhead'><div><h2>{E(snapshot.ApplicationId)} Configuration Runbook</h2><div class='muted'>Version {E(snapshot.ApplicationVersion)}</div></div><button class='btn primary' onclick='window.print()'>Print / Save PDF</button></div><pre>{E(markdown)}</pre></section>";
    return Html(Page("Runbook", $"Applications › {E(snapshot.ApplicationId)} › Runbook", body, "runbooks"));
});

app.MapPost("/api/contracts/register", async (ApplicationConfigurationContract contract, HttpContext context, IConfigurationContractRegistrationStore store, ContractValidator validator, ISecretProvider secrets, CancellationToken ct) =>
{
    ContractValidationResult validation = validator.Validate(contract);
    if (!validation.IsValid) return Results.BadRequest(validation);
    if (!DirectoryContractStore.IsSafeApplicationId(contract.ApplicationId)) return Results.BadRequest(new { error = "ApplicationId may contain only letters, digits, '.', '-' and '_'." });
    string providedKey = context.Request.Headers["X-Configuration-Registration-Key"].FirstOrDefault() ?? string.Empty;
    string? expectedKey = await secrets.GetAsync($"configuration/registration/{contract.ApplicationId}", ct);
    if (string.IsNullOrWhiteSpace(expectedKey) || !FixedTimeEquals(providedKey, expectedKey)) return Results.Unauthorized();
    try { return Results.Ok(await store.RegisterAsync(contract, ct)); }
    catch (InvalidDataException ex) { return Results.BadRequest(new { error = ex.Message }); }
});
app.MapGet("/api/contracts", async (IConfigurationContractStore store, CancellationToken ct) => Results.Ok(await store.LoadAllAsync(ct)));
app.MapGet("/api/report/{applicationId}", async (string applicationId, CommissioningService service, InstructionPackCatalog packs, CommissioningReportRenderer renderer, CancellationToken ct) => { CommissioningSnapshot? snapshot = (await service.EvaluateAllAsync(ct)).FirstOrDefault(x => string.Equals(x.ApplicationId, applicationId, StringComparison.OrdinalIgnoreCase)); return snapshot is null ? Results.NotFound() : Results.Text(renderer.RenderMarkdown(snapshot, await packs.FindAsync(snapshot.ApplicationId, snapshot.ApplicationVersion, ct)), "text/markdown"); });
app.MapPost("/api/contracts/validate", (ApplicationConfigurationContract contract, ContractValidator validator) => Results.Ok(validator.Validate(contract)));
app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "Aegis.Configuration" }));
app.Run();

static IResult Html(string value) => Results.Content(value, "text/html");
static string E(string? value) => HtmlEncoder.Default.Encode(value ?? "");
static bool FixedTimeEquals(string left, string right) { byte[] a = Encoding.UTF8.GetBytes(left); byte[] b = Encoding.UTF8.GetBytes(right); return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b); }
static string Active(string current, string item) => current == item ? " active" : string.Empty;

static string Page(string title, string crumb, string content, string active) => $$"""
<!doctype html><html><head><title>{{E(title)}} · Aegis Configuration</title><meta name="viewport" content="width=device-width,initial-scale=1,viewport-fit=cover"><style>
:root{--bg:#02070d;--panel:#071421;--panel2:#0b1c2b;--line:#27547d;--ink:#eaf3ff;--muted:#9db7d4;--orange:#ff9b2f;--blue:#7896ff;--green:#50e59a;--yellow:#ffd05c;--red:#ff7777}*{box-sizing:border-box}body{margin:0;background:var(--bg);color:var(--ink);font:14px/1.35 Arial,Helvetica,sans-serif}a{color:inherit;text-decoration:none}a:focus-visible,button:focus-visible{outline:3px solid #fff;outline-offset:2px}.shell{min-height:100vh;display:grid;grid-template-columns:92px 1fr}.rail{padding:10px 6px}.brandcap{height:52px;background:var(--blue);border-radius:28px 0 0 28px;margin-bottom:8px}.nav{display:grid;gap:5px}.nav a{min-height:70px;display:grid;place-items:center;text-align:center;background:var(--blue);color:#06101a;font-weight:800;border-radius:20px 0 0 20px;padding:6px}.nav a.active{background:var(--orange)}.main{padding:10px 14px 28px;min-width:0}.mast{border-bottom:4px solid var(--orange);padding:2px 0 8px}.title{font-size:clamp(24px,3vw,42px);font-weight:900}.title b,.sectiontitle{color:var(--orange)}.tag,.muted{color:var(--muted)}.tag{font-size:12px}.crumb{margin:8px 0;padding:7px 12px;border:1px solid var(--line);border-radius:16px;background:var(--panel)}.panel{border:2px solid var(--line);border-radius:14px;background:linear-gradient(180deg,var(--panel2),var(--panel));margin:9px 0;overflow:hidden}.appgrid{display:grid;grid-template-columns:minmax(240px,.9fr) minmax(210px,.75fr) minmax(210px,.65fr);gap:8px;padding:10px}.identity,.status,.actions{border:1px solid var(--line);border-radius:9px;padding:11px}.identity h2,.runhead h2{margin:0 0 2px;font-size:20px}.meta{display:grid;grid-template-columns:105px 1fr;gap:3px;margin-top:9px}.complete{color:var(--green);font-size:22px;font-weight:900}.incomplete{color:var(--yellow);font-size:22px;font-weight:900}.actions,.runbooklist{display:grid;gap:6px}.runbooklist{padding:10px}.btn{display:block;border:1px solid var(--blue);border-radius:8px;padding:10px 12px;text-align:center;background:transparent;color:var(--ink);font:inherit;cursor:pointer;min-height:42px}.btn.primary{background:#0d55d8}.sectiontitle{padding:7px 12px;font-weight:900;border-top:1px solid var(--line)}.table{display:grid}.tr{display:grid;grid-template-columns:1fr 1.6fr .55fr 1.25fr .8fr 1.25fr;border-top:1px solid #24425e}.tr>div{padding:8px 10px;min-width:0;overflow-wrap:anywhere}.thead{background:#122a3d;font-weight:800}.badge{display:inline-block;padding:3px 8px;border-radius:12px;font-weight:800}.required{background:var(--red);color:#101010}.optional{background:var(--blue);color:#06101a}.ok{color:var(--green)}.missing{color:var(--red);font-weight:800}.footer{margin-top:12px;border-top:3px solid var(--orange);padding:8px;color:var(--muted);display:flex;gap:18px;flex-wrap:wrap}.curveback{position:fixed;left:7px;top:12px;width:78px;height:40px;border:0;border-radius:22px 0 0 22px;background:rgba(2,7,13,.18);cursor:pointer;z-index:3}.curveback:after{content:'‹';color:#06101a;font-size:32px;font-weight:900;position:absolute;left:16px;top:-2px}.empty{padding:24px}.runbook{padding:14px}.runhead{display:flex;justify-content:space-between;gap:12px;align-items:center;margin-bottom:12px}.runbook pre{white-space:pre-wrap;overflow-wrap:anywhere;background:#030a11;border:1px solid var(--line);border-radius:9px;padding:16px;color:var(--ink);font:13px/1.5 ui-monospace,Consolas,monospace}.mobiletop,.mobilebottom{display:none}
@media(max-width:900px){.shell{grid-template-columns:72px 1fr}.nav a{min-height:62px;font-size:11px}.appgrid{grid-template-columns:1fr 1fr}.actions{grid-column:1/-1;grid-template-columns:repeat(3,1fr)}.tr{grid-template-columns:1fr .7fr 1.2fr .8fr}.tr .desc,.tr .example{display:none}.title{font-size:27px}}
@media(max-width:600px){body{font-size:13px}.shell{display:block}.rail{display:none}.mobiletop{display:flex;position:sticky;top:0;z-index:5;height:56px;align-items:center;gap:10px;padding:7px max(10px,env(safe-area-inset-left));background:#06111c;border-bottom:3px solid var(--orange)}.mobilemark{width:48px;height:34px;background:var(--blue);border-radius:18px 0 0 18px}.mobiletitle{font-weight:900;letter-spacing:.04em}.main{padding:8px 8px calc(78px + env(safe-area-inset-bottom))}.mast{display:none}.crumb{margin:5px 0}.appgrid{grid-template-columns:1fr;padding:7px}.actions{grid-column:auto;grid-template-columns:1fr}.identity,.status,.actions{padding:10px}.meta{grid-template-columns:90px 1fr}.table{gap:7px;padding:7px}.thead{display:none}.tr{display:grid;grid-template-columns:1fr auto;gap:5px;border:1px solid var(--line);border-radius:10px;background:#071522;padding:10px}.tr>div{padding:1px 2px}.tr .name{font-weight:900;font-size:15px}.tr .reqflag{grid-column:2;grid-row:1}.tr .desc{display:block;grid-column:1/-1;color:var(--muted)}.tr .value{grid-column:1/-1;color:var(--muted);font-size:12px;overflow-wrap:anywhere}.tr .ok,.tr .missing{grid-column:1/-1}.tr .example{display:block;grid-column:1/-1;font-size:12px;color:var(--muted)}.footer{display:none}.mobilebottom{display:grid;grid-template-columns:repeat(3,1fr);position:fixed;bottom:0;left:0;right:0;z-index:6;background:#06111c;border-top:3px solid var(--orange);padding:7px 7px calc(7px + env(safe-area-inset-bottom));gap:5px}.mobilebottom a{min-height:44px;display:grid;place-items:center;border:1px solid var(--line);border-radius:8px;font-size:11px;font-weight:800}.mobilebottom a.active{background:var(--orange);color:#06101a}.curveback{display:none}.runhead{display:block}.runhead .btn{margin-top:10px;width:100%}.runbook{padding:10px}.runbook pre{font-size:12px;padding:10px}.runbooklist .btn{text-align:left;min-height:48px}}
@media print{body{background:#fff;color:#000}.rail,.mast,.crumb,.footer,.curveback,.mobiletop,.mobilebottom,.runhead .btn{display:none!important}.shell{display:block}.main{padding:0}.panel,.runbook pre{border:0;background:#fff;color:#000}.runbook{margin:0;padding:0}}
</style></head><body><button class="curveback" aria-label="Go back" onclick="history.back()"></button><header class="mobiletop"><div class="mobilemark"></div><div class="mobiletitle">AEGIS CONFIGURATION</div></header><div class="shell"><aside class="rail"><div class="brandcap"></div><nav class="nav" aria-label="Primary"><a class="{{Active(active,"applications")}}" href="/">APPLICATIONS</a><a class="{{Active(active,"runbooks")}}" href="/runbooks">RUNBOOKS</a><a href="/api/contracts">CONTRACTS</a></nav></aside><main class="main"><header class="mast"><div class="title"><b>AEGIS</b> CONFIGURATION</div><div class="tag">KNOW WHAT IS REQUIRED. KNOW WHAT IS CONFIGURED.</div></header><div class="crumb">⌂ &nbsp; {{crumb}}</div>{{content}}<footer class="footer"><a href="/">Configuration</a><a href="/api/contracts">Contracts</a><span>A Safer Tomorrow</span></footer></main></div><nav class="mobilebottom" aria-label="Mobile navigation"><a class="{{Active(active,"applications")}}" href="/">APPLICATIONS</a><a class="{{Active(active,"runbooks")}}" href="/runbooks">RUNBOOKS</a><a href="/api/contracts">CONTRACTS</a></nav></body></html>
""";

static string RenderApplication(ApplicationConfigurationContract contract, ConfigurationEvaluation evaluation, bool detailsOnly = false)
{
    int required = contract.Requirements.Count(x => x.Required);
    int requiredConfigured = evaluation.Requirements.Count(x => x.Required && x.State == ConfigurationState.Configured);
    int optional = contract.Requirements.Count(x => !x.Required);
    int optionalConfigured = evaluation.Requirements.Count(x => !x.Required && x.State == ConfigurationState.Configured);
    bool complete = evaluation.IsComplete;
    string id = Uri.EscapeDataString(contract.ApplicationId);
    string requirements = string.Join("", evaluation.Requirements.Select(r => $"<div class='tr'><div class='name'>{E(r.DisplayName)}</div><div class='desc'>{E(r.Purpose)}</div><div class='reqflag'><span class='badge {(r.Required ? "required" : "optional")}'>{(r.Required ? "Required" : "Optional")}</span></div><div class='value'>{E(r.Sensitive ? "Protected / not displayed" : r.SafeDisplayValue ?? (r.State == ConfigurationState.Configured ? "Configured" : "Not configured"))}</div><div class='{(r.State == ConfigurationState.Missing ? "missing" : "ok")}'>{(r.State == ConfigurationState.Missing ? "● MISSING" : r.State == ConfigurationState.OptionalNotConfigured ? "○ OPTIONAL / NOT CONFIGURED" : "● CONFIGURED")}</div><div class='example'>{E(r.Example)}</div></div>"));
    string actions = detailsOnly ? $"<a class='btn primary' href='/runbooks/{id}'>View Runbook</a><a class='btn' href='/'>Back to Applications</a>" : $"<a class='btn primary' href='/applications/{id}'>Application Details</a><a class='btn' href='/runbooks/{id}'>View Runbook</a><a class='btn' href='/runbooks/{id}'>Export / Print Runbook</a>";
    return $"<section class='panel'><div class='appgrid'><div class='identity'><h2>{E(contract.DisplayName)}</h2><div class='muted'>{E(contract.Description ?? "Registered Aegis application")}</div><div class='meta'><b>Application ID</b><span>{E(contract.ApplicationId)}</span><b>Version</b><span>{E(contract.Version)}</span><b>Instance</b><span>{E(contract.InstanceId ?? "—")}</span><b>Site</b><span>{E(contract.SiteId ?? "—")}</span></div></div><div class='status'><div class='{(complete ? "complete" : "incomplete")}'>{(complete ? "✓ Complete" : "● Incomplete")}</div><p>{requiredConfigured}/{required} required &nbsp; {optionalConfigured}/{optional} optional</p><div class='muted'>{(complete ? "All required configuration values are supplied." : "One or more required configuration values are missing.")}</div></div><div class='actions'>{actions}</div></div><div class='sectiontitle'>Configuration Requirements</div><div class='table'><div class='tr thead'><div>Setting Name</div><div class='desc'>Description</div><div>Requirement</div><div>Current Value</div><div>Status</div><div class='example'>Example / Notes</div></div>{requirements}</div></section>";
}
