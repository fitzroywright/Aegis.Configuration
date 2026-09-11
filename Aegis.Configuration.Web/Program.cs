using System.Text.Encodings.Web;
using Aegis.Configuration.Application;
using Aegis.Configuration.Domain;
using Aegis.Configuration.Infrastructure;
using Common.Secrets;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
string contractDirectory = builder.Configuration["AegisConfiguration:ContractDirectory"] ?? Path.Combine(builder.Environment.ContentRootPath, "contracts");
string dataDirectory = builder.Configuration["AegisConfiguration:DataDirectory"] ?? Path.Combine(builder.Environment.ContentRootPath, "data");
string instructionDirectory = builder.Configuration["AegisConfiguration:InstructionDirectory"] ?? Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "..", "..", "instruction-packs"));

builder.Services.AddCommonSecrets(builder.Configuration);
builder.Services.AddCommonSecretsDiagnostics();
builder.Services.AddSingleton<IConfigurationContractStore>(new DirectoryContractStore(contractDirectory));
builder.Services.AddSingleton<ICommissioningHistoryStore>(new JsonCommissioningHistoryStore(Path.Combine(dataDirectory, "commissioning-history.jsonl")));
builder.Services.AddSingleton(new InstructionPackCatalog(instructionDirectory));
builder.Services.AddSingleton<ISecretValueResolver, CommonSecretsValueResolver>();
builder.Services.AddSingleton<IConfigurationValueResolver, RuntimeConfigurationValueResolver>();
builder.Services.AddSingleton<IConfigurationRequirementValidator, SecretResolvedRequirementValidator>();
builder.Services.AddSingleton<IConfigurationRequirementValidator, UriRequirementValidator>();
builder.Services.AddHttpClient<HttpEndpointRequirementValidator>();
builder.Services.AddSingleton<IConfigurationRequirementValidator>(p => p.GetRequiredService<HttpEndpointRequirementValidator>());
builder.Services.AddSingleton<IConfigurationRequirementValidator, TcpEndpointRequirementValidator>();
builder.Services.AddSingleton<IConfigurationRequirementValidator, PostgreSqlRequirementValidator>();
builder.Services.AddSingleton<ConfigurationEvaluator>();
builder.Services.AddSingleton<CommissioningService>();
builder.Services.AddSingleton<CommissioningRecorder>();
builder.Services.AddSingleton<CommissioningReportRenderer>();
builder.Services.AddSingleton<ConfigurationReadinessScorer>();
builder.Services.AddSingleton<DriftDetector>();
builder.Services.AddSingleton<FleetStatusCache>();
builder.Services.AddSingleton<ContractValidator>();
WebApplication app = builder.Build();

app.MapGet("/", (FleetStatusCache cache, ConfigurationReadinessScorer scorer) =>
{
    FleetStatusSnapshot? fleet = cache.Current;
    string cards = fleet is null ? "<section class='panel'><h2>NO FLEET SNAPSHOT</h2><p>Run VERIFY FLEET to execute commissioning verification.</p></section>" : string.Join("", fleet.Applications.Select(x => RenderApplication(x, scorer.Calculate(x.Evaluation))));
    string stamp = fleet is null ? "UNVERIFIED" : fleet.CapturedAtUtc.ToString("yyyy-MM-dd HH:mm:ss 'UTC'");
    return Results.Content($$"""
<!doctype html><html><head><title>AEGIS CONFIGURATION // LCARS</title><meta name="viewport" content="width=device-width,initial-scale=1"><style>
:root{--bg:#050509;--ink:#f6f1ff;--orange:#ff9f40;--salmon:#ff7f7f;--lav:#c7a7ff;--blue:#7aa7ff;--gold:#ffd166;--green:#72e6a0;--dim:#2a2740}*{box-sizing:border-box}body{margin:0;background:var(--bg);color:var(--ink);font-family:Arial,Helvetica,sans-serif;letter-spacing:.04em}main{max-width:1400px;margin:auto;padding:18px}.lcars{display:grid;grid-template-columns:170px 1fr;gap:12px}.rail{background:var(--orange);border-radius:80px 0 0 80px;min-height:90vh;padding-top:100px;color:#080808;font-weight:900}.rail div{padding:12px 18px;border-top:8px solid var(--bg);text-align:right}.rail .salmon{background:var(--salmon)}.rail .lav{background:var(--lav)}.rail .blue{background:var(--blue)}.top{display:flex;align-items:end;gap:8px;margin-bottom:14px}.cap{height:54px;border-radius:0 32px 32px 0;background:var(--orange);width:160px}.title{font-size:clamp(25px,4vw,52px);font-weight:900;flex:1;text-align:right}.sub{color:var(--lav);font-size:13px}.actions{display:flex;gap:8px;flex-wrap:wrap;margin:12px 0}.btn{border:0;border-radius:22px;padding:11px 20px;background:var(--orange);color:#080808;font-weight:900;cursor:pointer;text-decoration:none}.btn.blue{background:var(--blue)}.btn.lav{background:var(--lav)}.panel{border-top:10px solid var(--lav);background:#101018;padding:18px;margin:14px 0;border-radius:0 0 18px 18px}.apphead{display:flex;justify-content:space-between;gap:20px;align-items:center}.score{font-size:38px;font-weight:900}.Green{color:var(--green)}.Yellow{color:var(--gold)}.Orange{color:var(--orange)}.Red{color:var(--salmon)}.req{display:grid;grid-template-columns:1.1fr .55fr 2fr;gap:12px;padding:10px 0;border-top:2px solid var(--dim)}.pill{display:inline-block;padding:5px 10px;border-radius:14px;background:var(--dim)}.Verified{background:#174f35}.Missing{background:#692929}.Invalid{background:#713d19}.ConfiguredUnverified{background:#665716}.muted{color:#aaa5bc}a{color:var(--blue)}@media(max-width:760px){.lcars{grid-template-columns:42px 1fr}.rail div{font-size:0;padding:12px 4px}.req{grid-template-columns:1fr}.cap{width:55px}}
</style></head><body><main><div class="lcars"><aside class="rail"><div>CONFIG</div><div class="salmon">STATUS</div><div class="lav">DRIFT</div><div class="blue">SETUP</div></aside><section><div class="top"><div class="cap"></div><div><div class="title">AEGIS CONFIGURATION</div><div class="sub">LCARS // APPLICATION COMMISSIONING & CONFIGURATION</div></div></div><div class="actions"><form method="post" action="/verify"><button class="btn">VERIFY FLEET</button></form><a class="btn blue" href="/api/history">HISTORY</a><a class="btn lav" href="/api/contracts">CONTRACTS</a></div><div class="sub">LAST VERIFIED: {{stamp}}</div>{{cards}}</section></div></main></body></html>
""", "text/html");
});

app.MapPost("/verify", async (CommissioningService service, FleetStatusCache cache, CancellationToken ct) => { IReadOnlyList<CommissioningSnapshot> snapshots = await service.EvaluateAllAsync(ct); cache.Replace(snapshots); return Results.Redirect("/"); });
app.MapPost("/api/verify", async (CommissioningService service, FleetStatusCache cache, CancellationToken ct) => Results.Ok(cache.Replace(await service.EvaluateAllAsync(ct))));
app.MapGet("/api/fleet", (FleetStatusCache cache) => cache.Current is null ? Results.NoContent() : Results.Ok(cache.Current));
app.MapGet("/api/contracts", async (IConfigurationContractStore store, CancellationToken ct) => Results.Ok(await store.LoadAllAsync(ct)));
app.MapGet("/api/history", async (ICommissioningHistoryStore history, CancellationToken ct) => Results.Ok(await history.ReadAsync(null, ct)));
app.MapGet("/api/commissioning", async (CommissioningService service, CancellationToken ct) => Results.Ok(await service.EvaluateAllAsync(ct)));
app.MapPost("/api/commission/{applicationId}", async (string applicationId, CommissioningService service, CommissioningRecorder recorder, CancellationToken ct) => { CommissioningSnapshot? snapshot = (await service.EvaluateAllAsync(ct)).FirstOrDefault(x => string.Equals(x.ApplicationId, applicationId, StringComparison.OrdinalIgnoreCase)); return snapshot is null ? Results.NotFound() : Results.Ok(await recorder.RecordAsync(snapshot, ct)); });
app.MapGet("/api/report/{applicationId}", async (string applicationId, CommissioningService service, InstructionPackCatalog packs, CommissioningReportRenderer renderer, CancellationToken ct) => { CommissioningSnapshot? snapshot = (await service.EvaluateAllAsync(ct)).FirstOrDefault(x => string.Equals(x.ApplicationId, applicationId, StringComparison.OrdinalIgnoreCase)); if (snapshot is null) return Results.NotFound(); return Results.Text(renderer.RenderMarkdown(snapshot, await packs.FindAsync(snapshot.ApplicationId, snapshot.ApplicationVersion, ct)), "text/markdown"); });
app.MapGet("/api/instructions/{applicationId}", async (string applicationId, CommissioningService service, InstructionPackCatalog packs, CancellationToken ct) => { CommissioningSnapshot? snapshot = (await service.EvaluateAllAsync(ct)).FirstOrDefault(x => string.Equals(x.ApplicationId, applicationId, StringComparison.OrdinalIgnoreCase)); return snapshot is null ? Results.NotFound() : Results.Ok(await packs.FindAsync(snapshot.ApplicationId, snapshot.ApplicationVersion, ct)); });
app.MapGet("/api/drift/{applicationId}", async (string applicationId, CommissioningService service, ICommissioningHistoryStore history, DriftDetector detector, CancellationToken ct) => { CommissioningSnapshot? current = (await service.EvaluateAllAsync(ct)).FirstOrDefault(x => string.Equals(x.ApplicationId, applicationId, StringComparison.OrdinalIgnoreCase)); CommissioningHistoryEntry? previous = (await history.ReadAsync(applicationId, ct)).FirstOrDefault(); if (current is null) return Results.NotFound(); return previous is null ? Results.Ok(new { applicationId, status = "No baseline recorded" }) : Results.Ok(detector.Compare(previous, current)); });
app.MapPost("/api/contracts/validate", (ApplicationConfigurationContract contract, ContractValidator validator) => Results.Ok(validator.Validate(contract)));
app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "Aegis.Configuration" }));
app.Run();

static string RenderApplication(CommissioningSnapshot snapshot, ConfigurationReadinessScore score)
{
    HtmlEncoder e = HtmlEncoder.Default;
    string requirements = string.Join("", snapshot.Evaluation.Requirements.Select(r => $"<div class='req'><strong>{e.Encode(r.DisplayName)}</strong><div><span class='pill {r.State}'>{r.State}</span></div><div class='muted'>{e.Encode(r.Message)}</div></div>"));
    string id = Uri.EscapeDataString(snapshot.ApplicationId);
    return $"<section class='panel'><div class='apphead'><div><h2>{e.Encode(snapshot.ApplicationId)} <span class='muted'>V{e.Encode(snapshot.ApplicationVersion)}</span></h2><div>{(snapshot.Evaluation.IsOperational ? "CONFIGURATION READY" : "COMMISSIONING REQUIRED")}</div></div><div class='score {score.Grade}'>{score.Score:D3}%</div></div><p><a href='/api/instructions/{id}'>INSTRUCTIONS</a> // <a href='/api/report/{id}'>REPORT</a> // <a href='/api/drift/{id}'>DRIFT</a></p>{requirements}</section>";
}
