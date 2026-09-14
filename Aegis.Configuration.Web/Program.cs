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
    return Html(Shell("Runbooks", $"<section class='page-head'><div><h1>Runbooks</h1><p>Configuration commissioning and recovery instructions</p></div></section><section class='panel'><h2>Configuration Runbooks</h2>{cards}</section>", "runbooks"));
});
app.MapGet("/runbooks/{applicationId}", async (string applicationId, CommissioningService service, InstructionPackCatalog packs, CommissioningReportRenderer renderer, CancellationToken ct) =>
{
    var s = (await service.EvaluateAllAsync(ct)).FirstOrDefault(x => string.Equals(x.ApplicationId, applicationId, StringComparison.OrdinalIgnoreCase));
    if (s is null) return Results.NotFound();
    string markdown = renderer.RenderMarkdown(s, await packs.FindAsync(s.ApplicationId, s.ApplicationVersion, ct));
    return Html(Shell("Runbook", $"<section class='page-head'><div><h1>{E(s.ApplicationId)} Runbook</h1><p>Version {E(s.ApplicationVersion)}</p></div></section><section class='panel'><pre>{E(markdown)}</pre></section>", "runbooks"));
});
app.MapPost("/api/contracts/register", async (ApplicationConfigurationContract contract, HttpContext context, IConfigurationContractRegistrationStore store, ContractValidator validator, ISecretProvider secrets, CancellationToken ct) =>
{
    var v = validator.Validate(contract); if (!v.IsValid) return Results.BadRequest(v);
    if (!DirectoryContractStore.IsSafeApplicationId(contract.ApplicationId)) return Results.BadRequest(new { error = "ApplicationId may contain only letters, digits, '.', '-' and '_'." });
    string supplied = context.Request.Headers["X-Configuration-Registration-Key"].FirstOrDefault() ?? "";
    string? expected = await secrets.GetAsync($"configuration/registration/{contract.ApplicationId}", ct);
    if (string.IsNullOrWhiteSpace(expected) || !FixedTimeEquals(supplied, expected)) return Results.Unauthorized();
    try { return Results.Ok(await store.RegisterAsync(contract, ct)); } catch (InvalidDataException ex) { return Results.BadRequest(new { error = ex.Message }); }
});
app.MapGet("/api/contracts", async (IConfigurationContractStore store, CancellationToken ct) => Results.Ok(await store.LoadAllAsync(ct)));
app.MapGet("/api/report/{applicationId}", async (string applicationId, CommissioningService service, InstructionPackCatalog packs, CommissioningReportRenderer renderer, CancellationToken ct) => { var s = (await service.EvaluateAllAsync(ct)).FirstOrDefault(x => string.Equals(x.ApplicationId, applicationId, StringComparison.OrdinalIgnoreCase)); return s is null ? Results.NotFound() : Results.Text(renderer.RenderMarkdown(s, await packs.FindAsync(s.ApplicationId, s.ApplicationVersion, ct)), "text/markdown"); });
app.MapPost("/api/contracts/validate", (ApplicationConfigurationContract c, ContractValidator v) => Results.Ok(v.Validate(c)));
app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "Aegis.Configuration", displayName = "System Configuration" }));
app.Run();

static IResult Html(string value) => Results.Content(value, "text/html");
static string E(string? value) => HtmlEncoder.Default.Encode(value ?? "");
static bool FixedTimeEquals(string left, string right) { byte[] a = Encoding.UTF8.GetBytes(left), b = Encoding.UTF8.GetBytes(right); return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b); }
static string Active(string current, string item) => current == item ? " active" : "";

static string RenderDashboard(List<(ApplicationConfigurationContract Contract, ConfigurationEvaluation Evaluation)> rows, int? selectedIndex)
{
    int total = rows.Count, configured = rows.Count(x => x.Evaluation.IsComplete), requirements = rows.Sum(x => x.Contract.Requirements.Count);
    int selected = selectedIndex ?? (rows.Count == 0 ? -1 : 0);
    string tableRows = rows.Count == 0 ? "<tr><td colspan='8' class='empty'>No applications registered.</td></tr>" : string.Join("", rows.Select((x, i) => RenderRow(x.Contract, x.Evaluation, i == selected)));
    string detail = selected < 0 ? "<div class='empty'>No application selected.</div>" : RenderDetail(rows[selected].Contract, rows[selected].Evaluation);
    string activity = rows.Count == 0 ? "<div class='empty'>No registration activity.</div>" : string.Join("", rows.OrderByDescending(x => x.Contract.LastRegisteredAtUtc).Take(5).Select(x => $"<div class='activity'><i class='dot {(x.Evaluation.IsComplete ? "green" : "amber")}'></i><span>{E(x.Contract.LastRegisteredAtUtc?.ToLocalTime().ToString("MMM dd, yyyy HH:mm") ?? "Legacy")}</span><b>{E(x.Contract.DisplayName)}</b><span>{(x.Evaluation.IsComplete ? "Registration complete" : "Configuration incomplete")}</span></div>"));
    string content = $"""
<section class='page-head'><div><h1>Applications</h1><p>All registered applications and their configuration status</p></div><div class='toolbar'><label class='search'>⌕ <input id='search' placeholder='Search applications...' oninput='filterRows()'></label><select id='statusFilter' onchange='filterRows()'><option>All Status</option><option>Configured</option><option>Not Configured</option></select><button onclick='location.reload()'>↻</button><button class='primary' onclick='document.getElementById("registerInfo").showModal()'>＋ Register Application</button></div></section>
<section class='workspace'><div class='leftcol'><div class='metrics'><div class='metric blue'><span>◇</span><b>{total}</b><small>Total Applications</small></div><div class='metric greenbox'><span>✓</span><b>{configured}</b><small>Configured</small></div><div class='metric amberbox'><span>▲</span><b>{total-configured}</b><small>Not Configured</small></div><div class='metric blue'><span>☷</span><b>{requirements}</b><small>Total Requirements</small></div></div>
<div class='tablewrap'><table id='apps'><thead><tr><th>Application</th><th>Version</th><th>Site / Instance</th><th>Status</th><th>Required</th><th>Optional</th><th>Last Updated</th><th></th></tr></thead><tbody>{tableRows}</tbody></table></div>
<div class='bottomgrid'><section class='mini'><header>◴ Recent Activity <span>View All</span></header>{activity}</section><section class='mini links'><header>🔗 Quick Links</header><a href='/runbooks'>▤ View All Runbooks</a><a href='/api/contracts'>⇩ Download Contracts</a><a href='/api/contracts'>▣ Export Configuration Report</a><a href='#'>? Configuration Help</a></section></div></div><aside class='detail'>{detail}</aside></section>
<dialog id='registerInfo'><h2>Register Application</h2><p>Applications register securely through the Configuration contract API.</p><code>POST /api/contracts/register</code><button class='primary' onclick='this.closest("dialog").close()'>Close</button></dialog>
<script>const filterRows=()=>document.querySelectorAll('#apps tbody tr[data-name]').forEach(r=>r.style.display=(!document.getElementById('search').value||r.dataset.name.includes(document.getElementById('search').value.toLowerCase()))&&(document.getElementById('statusFilter').value==='All Status'||r.dataset.status===document.getElementById('statusFilter').value)?'':'none');</script>
""";
    return Shell("Applications", content, "applications");
}

static string RenderRow(ApplicationConfigurationContract c, ConfigurationEvaluation e, bool selected)
{
    int req = c.Requirements.Count(x => x.Required), reqOk = e.Requirements.Count(x => x.Required && x.State == ConfigurationState.Configured);
    int opt = c.Requirements.Count(x => !x.Required), optOk = e.Requirements.Count(x => !x.Required && x.State == ConfigurationState.Configured);
    string status = e.IsComplete ? "Configured" : "Not Configured", cls = e.IsComplete ? "good" : "warn";
    return $"<tr class='{(selected ? "selected" : "")}' data-name='{E(c.DisplayName.ToLowerInvariant())}' data-status='{status}' onclick=\"location.href='/applications/{Uri.EscapeDataString(c.ApplicationId)}'\"><td><span class='appicon'>{IconFor(c.DisplayName)}</span><b>{E(c.DisplayName)}</b></td><td>{E(c.Version)}</td><td>{E(c.SiteId ?? "—")} / {E(c.InstanceId ?? "—")}</td><td><span class='pill {cls}'>{(e.IsComplete ? "✓" : "▲")} {status}</span></td><td>{reqOk}/{req}</td><td>{optOk}/{opt}</td><td>{E(c.LastRegisteredAtUtc?.ToLocalTime().ToString("MMM dd HH:mm") ?? "Legacy")}</td><td>›</td></tr>";
}

static string RenderDetail(ApplicationConfigurationContract c, ConfigurationEvaluation e)
{
    int required = c.Requirements.Count(x => x.Required), optional = c.Requirements.Count(x => !x.Required);
    string id = Uri.EscapeDataString(c.ApplicationId), status = e.IsComplete ? "Configured" : "Not Configured", cls = e.IsComplete ? "good" : "warn";
    string requiredRows = RenderRequirements(e.Requirements.Where(x => x.Required));
    string optionalRows = RenderRequirements(e.Requirements.Where(x => !x.Required));
    return $"""
<div class='detailhead'><span class='heroicon'>{IconFor(c.DisplayName)}</span><div><h2>{E(c.DisplayName)}</h2><p>{E(c.Description ?? "Registered application")}</p></div><span class='pill {cls}'>{(e.IsComplete ? "✓" : "▲")} {status}</span></div>
<div class='facts'><div><small>Version</small><b>{E(c.Version)}</b></div><div><small>Site / Instance</small><b>{E(c.SiteId ?? "—")} / {E(c.InstanceId ?? "—")}</b></div><div><small>Registered</small><b>{E(c.LastRegisteredAtUtc?.ToLocalTime().ToString("MMM dd, yyyy HH:mm") ?? "Legacy")}</b></div><div><small>Application ID</small><b>{E(c.ApplicationId)}</b></div></div>
<div class='tabs'><button class='tab active' onclick='showTab(event,"requirements")'>Requirements ({required})</button><button class='tab' onclick='showTab(event,"details")'>Details</button><a class='tab' href='/runbooks/{id}'>Runbooks</a><a class='tab' href='/api/contracts'>Contract</a></div>
<div id='requirements' class='tabpane'><div class='reqtable'><div class='reqrow head'><span>Name</span><span>Description</span><span>Value</span><span>Status</span></div>{requiredRows}</div><h3>Optional Requirements ({optional})</h3>{(optional == 0 ? "<div class='optional-empty'>✓ No optional requirements defined.</div>" : $"<div class='reqtable'>{optionalRows}</div>")}</div>
<div id='details' class='tabpane hidden'><dl><dt>Application ID</dt><dd>{E(c.ApplicationId)}</dd><dt>Version</dt><dd>{E(c.Version)}</dd><dt>Site</dt><dd>{E(c.SiteId ?? "—")}</dd><dt>Instance</dt><dd>{E(c.InstanceId ?? "—")}</dd><dt>Status</dt><dd>{status}</dd></dl></div>
<div class='detailactions'><a href='/api/contracts'>▣ View Contract</a><a href='/runbooks/{id}'>▤ View Runbooks</a><button onclick='window.print()'>▣ Print / PDF</button></div>
<script>const showTab=(e,id)=>(document.querySelectorAll('.tabpane').forEach(x=>x.classList.add('hidden')),document.getElementById(id).classList.remove('hidden'),document.querySelectorAll('.tab').forEach(x=>x.classList.remove('active')),e.currentTarget.classList.add('active'));</script>
""";
}

static string RenderRequirements(IEnumerable<ConfigurationRequirementEvaluation> requirements) => string.Join("", requirements.Select(r => $"<div class='reqrow'><span><b>{E(r.DisplayName)}</b></span><span>{E(r.Purpose)}</span><span>{E(r.Sensitive ? "Protected / not displayed" : r.SafeDisplayValue ?? (r.State == ConfigurationState.Configured ? "Configured" : "Not configured"))}</span><span><i class='pill {(r.State == ConfigurationState.Configured ? "good" : "warn")}'>{(r.State == ConfigurationState.Configured ? "✓ Configured" : "▲ Not Configured")}</i></span></div>"));
static string IconFor(string name) { string n = name.ToLowerInvariant(); return n.Contains("cafeteria") ? "♜" : n.Contains("studio") ? "▣" : n.Contains("sensor") ? "⌘" : n.Contains("diagnostic") ? "⌁" : n.Contains("request") ? "▤" : n.Contains("operation") ? "⌘" : "◆"; }

static string Shell(string title, string content, string active) => $"""
<!doctype html><html><head><meta charset='utf-8'><meta name='viewport' content='width=device-width,initial-scale=1'><title>{E(title)} · System Configuration</title><style>{Css()}\n{GlossyCss.Value}</style></head><body>
<header class='topbar'><div class='cornerbars'><i></i><i></i></div><div class='brand'><span class='gear'>✹</span><div><strong>System <em>Configuration</em></strong><small>KNOW WHAT IS REQUIRED. KNOW WHAT IS CONFIGURED.</small></div></div><div class='org'><b>Food For The Poor Jamaica</b><span>A More Hopeful Tomorrow</span></div><div class='site'>FFP-JM</div><div class='clock' id='clock'></div><div class='user'><span>FW</span><div>Fitzroy Wright<small>IT Manager</small></div></div></header>
<div class='frame'><aside class='sidebar'><nav><a class='{Active(active,"applications")}' href='/'><span>▦</span>Applications</a><a class='{Active(active,"runbooks")}' href='/runbooks'><span>▤</span>Runbooks</a><a href='/api/contracts'><span>▣</span>Contracts</a><a href='#'><span>⚙</span>Settings</a></nav><div class='sidefoot'><b>System<br>Configuration</b><small>v1.0.0</small></div></aside><main>{content}</main></div>
<footer><b>System Configuration</b><span>│</span><span>Integrated Operations for a More Reliable Tomorrow</span><div class='footright'><i class='dot green'></i> All Systems Operational &nbsp; │ &nbsp; Food For The Poor Jamaica &nbsp; │ &nbsp; v1.0.0</div></footer>
<script>const tick=()=>document.getElementById('clock').textContent=new Date().toLocaleString();tick();setInterval(tick,30000);</script></body></html>
""";

static string Css() => """
:root{--bg:#00101d;--panel:#001827;--line:#15547b;--text:#eef6ff;--muted:#aac2d7;--blue:#087fff;--orange:#ffad31;--purple:#8e61ff;--green:#20d789;--amber:#ff9f0b}*{box-sizing:border-box}html,body{margin:0;min-height:100%;background:#00101d;color:var(--text);font:14px/1.35 'Segoe UI',Arial,sans-serif}body{padding:0 5px}a{color:inherit;text-decoration:none}.topbar{height:84px;display:grid;grid-template-columns:70px minmax(380px,1fr) 220px 110px 220px 210px;align-items:center;border-bottom:2px solid #4180aa}.cornerbars{height:100%;display:flex;gap:7px;padding-top:18px}.cornerbars i:first-child{width:31px;height:54px;background:var(--orange);border-radius:26px 0 0}.cornerbars i:last-child{width:25px;height:54px;background:var(--purple)}.brand{display:flex;align-items:center;gap:14px}.gear{font-size:50px;color:#2ba9ff}.brand strong{font-size:34px;display:block}.brand em{font-style:normal;color:var(--orange)}.brand small{color:#42bdff;font-weight:800}.org{color:#5bc2ff;display:flex;flex-direction:column;font-size:16px}.site{background:#2727a6;border-left:18px solid var(--orange);border-radius:18px;padding:13px 18px;font-weight:800}.clock{border:1px solid #154c70;padding:14px}.user{display:flex;gap:10px;align-items:center;border:1px solid #154c70;border-radius:18px;padding:8px;color:#5bc2ff}.user>span{border:2px solid #209ef0;border-radius:50%;padding:9px}.user small{display:block}.frame{min-height:calc(100vh - 132px);border:1px solid #4c84a7;border-radius:25px;box-shadow:inset 0 0 24px #4db9ff44;display:grid;grid-template-columns:160px 1fr;overflow:hidden;margin-top:7px}.sidebar{background:#001b2e;border-right:1px solid #174f73;display:flex;flex-direction:column}.sidebar:before{content:'';height:63px}.sidebar a{display:flex;align-items:center;gap:17px;padding:16px;font-weight:700;font-size:16px}.sidebar a span{color:#36b9ff;font-size:25px}.sidebar a.active{background:var(--orange);color:#06111b;border-radius:0 9px 9px 0}.sidefoot{margin-top:auto;padding:25px;color:#59c0ff}.sidefoot small{display:block}main{padding:20px 10px;min-width:0}.page-head{display:flex;justify-content:space-between;align-items:center;gap:20px;padding:4px 12px 14px 40px;border-left:13px solid var(--purple);border-radius:18px}.page-head h1{font-size:31px;margin:0}.page-head p{margin:2px 0;color:#c1d4e4}.toolbar{display:flex;gap:10px}.search,.toolbar select,.toolbar button{background:#00182a;color:white;border:1px solid #23618a;border-radius:6px;padding:11px}.search input{background:none;border:0;outline:0;color:white;width:230px}.primary{background:var(--blue)!important;font-weight:800}.workspace{display:grid;grid-template-columns:minmax(650px,1.05fr) minmax(520px,.8fr);gap:14px}.metrics{display:grid;grid-template-columns:repeat(4,1fr);gap:13px;margin-bottom:16px}.metric{height:72px;border:1px solid #087fff;border-radius:5px;display:grid;grid-template-columns:55px 1fr;grid-template-rows:1fr 1fr;align-items:center;padding:8px;background:#002037}.metric span{grid-row:1/3;width:42px;height:42px;border-radius:50%;display:grid;place-items:center;font-size:24px;background:#126dff}.metric b{font-size:22px}.metric.greenbox{border-color:#0a9b66;background:#003626}.metric.greenbox span{background:var(--green)}.metric.amberbox{border-color:#9c6a0a;background:#332509}.metric.amberbox span{background:var(--amber)}.tablewrap,.mini,.detail,.panel{border:1px solid #21608a;border-radius:9px;background:#001522;overflow:hidden}table{border-collapse:collapse;width:100%;font-size:12px}th{text-align:left;background:#0b2b43;padding:11px}td{padding:7px;border-bottom:1px solid #16445f;white-space:nowrap}tr:hover,tr.selected{background:#07375b;outline:1px solid #0b8cff}.appicon{display:inline-grid;place-items:center;width:32px;height:32px;border-radius:5px;background:linear-gradient(135deg,#146eff,#7c4dff);margin-right:9px}.pill{display:inline-block;border:1px solid;border-radius:5px;padding:5px 8px;font-style:normal;font-weight:700}.pill.good{background:#00663f;border-color:#0c9e67}.pill.warn{color:#ffd56d;background:#5c3d00;border-color:#bd7900}.bottomgrid{display:grid;grid-template-columns:1.7fr 1fr;gap:12px;margin-top:12px}.mini{min-height:185px;padding:10px 17px}.mini header{font-size:16px;font-weight:800;border-bottom:1px solid #205171;padding-bottom:8px}.mini header span{float:right;color:#38b8ff}.activity{display:grid;grid-template-columns:15px 130px 150px 1fr;gap:8px;padding:7px 0;border-bottom:1px solid #163d57;font-size:11px}.dot{display:inline-block;width:13px;height:13px;border-radius:50%}.green{background:var(--green)}.amber{background:var(--amber)}.links a{display:block;padding:8px 0;color:#2db5ff}.detail{padding:15px;display:flex;flex-direction:column;min-height:675px}.detailhead{display:grid;grid-template-columns:62px 1fr auto;gap:12px;align-items:center;border-bottom:1px solid #24516c;padding-bottom:13px}.detailhead h2{margin:0;font-size:25px}.detailhead p{margin:2px 0;color:#d2e1ec}.heroicon{width:58px;height:58px;border-radius:9px;background:#087fff;display:grid;place-items:center;font-size:30px}.facts{display:grid;grid-template-columns:repeat(4,1fr);margin:10px 0 15px}.facts div{padding:8px;border-right:1px solid #173f58}.facts small,.facts b{display:block}.tabs{display:grid;grid-template-columns:repeat(4,1fr);border:1px solid #1d5478}.tab{background:#062139;color:white;border:0;border-right:1px solid #1d5478;padding:13px;text-align:center}.tab.active{background:#087fff}.reqtable{border:1px solid #205a7e;border-radius:7px;overflow:hidden;margin-top:14px}.reqrow{display:grid;grid-template-columns:1fr 1.5fr 1.25fr 1fr;border-bottom:1px solid #16445f}.reqrow span{padding:9px;border-right:1px solid #16445f;font-size:11px}.reqrow.head{background:#0a2941}.optional-empty{border:1px solid #205a7e;border-radius:7px;padding:28px;text-align:center}.hidden{display:none}.detailactions{margin-top:auto;border-top:1px solid #194862;padding-top:12px;display:flex;gap:8px}.detailactions>*{flex:1;background:#03213a;color:white;border:1px solid #087fff;border-radius:5px;padding:12px;text-align:center}.panel{padding:20px}.runbook-card{display:grid;grid-template-columns:50px 1fr 30px;align-items:center;padding:15px;border-top:1px solid #1d5478}.runbook-card small{display:block;color:#aac2d7}.panel pre{white-space:pre-wrap}.empty{padding:20px;text-align:center;color:#aac2d7}dialog{background:#001827;color:white;border:1px solid #168de0;border-radius:10px;padding:25px}dialog code{display:block;margin:15px 0}dl{display:grid;grid-template-columns:140px 1fr;gap:8px}dt{color:#55bfff}dd{margin:0}footer{height:42px;display:flex;align-items:center;gap:15px;padding:0 42px;border-top:10px solid var(--purple);color:#55bfff;font-size:12px}footer b{color:white}.footright{margin-left:auto}@media(max-width:1200px){.topbar{grid-template-columns:60px 1fr 110px 170px}.org,.user{display:none}.workspace{grid-template-columns:1fr}.metrics{grid-template-columns:repeat(2,1fr)}}@media(max-width:720px){body{padding:0}.topbar{height:68px;grid-template-columns:45px 1fr 85px}.brand strong{font-size:22px}.brand small,.clock{display:none}.gear{font-size:34px}.site{font-size:12px;padding:9px}.frame{margin:0;border-radius:0;grid-template-columns:58px 1fr}.sidebar:before{height:20px}.sidebar a{font-size:0;padding:14px 10px}.sidebar a span{font-size:23px}.sidefoot{display:none}main{padding:8px}.page-head{display:block;padding-left:15px}.toolbar{margin-top:10px;flex-wrap:wrap}.search{width:100%}.metrics{grid-template-columns:repeat(2,1fr)}.tablewrap{overflow-x:auto}.bottomgrid{grid-template-columns:1fr}.facts{grid-template-columns:repeat(2,1fr)}.reqrow{grid-template-columns:1fr 1fr}.reqrow span:nth-child(2){display:none}.detailactions{flex-wrap:wrap}footer{padding:0 10px}.footright{display:none}}
""";