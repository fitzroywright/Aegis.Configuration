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
builder.Services.AddSingleton<ContractValidator>();

WebApplication app = builder.Build();

app.MapGet("/", async (CommissioningService commissioning, ConfigurationReadinessScorer scorer, CancellationToken ct) =>
{
    IReadOnlyList<CommissioningSnapshot> snapshots = await commissioning.EvaluateAllAsync(ct);
    string cards = string.Join("", snapshots.Select(x => RenderApplication(x, scorer.Calculate(x.Evaluation))));
    if (cards.Length == 0) cards = "<section class='card'><h2>No application contracts discovered</h2></section>";
    return Results.Content($$"""
<!doctype html><html><head><title>Aegis.Configuration</title><meta name="viewport" content="width=device-width,initial-scale=1"><style>
body{font-family:system-ui;margin:0;background:#080d18;color:#e5e7eb}main{max-width:1250px;margin:auto;padding:32px}.head{display:flex;justify-content:space-between;align-items:end}.muted{color:#94a3b8}.card{background:#111827;border:1px solid #334155;border-radius:14px;padding:22px;margin:18px 0}.score{font-size:34px;font-weight:800}.Red{color:#f87171}.Orange{color:#fb923c}.Yellow{color:#facc15}.Green{color:#4ade80}.req{display:grid;grid-template-columns:1.2fr .6fr 2fr;gap:14px;padding:11px 0;border-top:1px solid #263244}.pill{padding:4px 9px;border-radius:999px;background:#334155}.Verified{background:#14532d}.Missing{background:#7f1d1d}.Invalid{background:#9a3412}.ConfiguredUnverified{background:#854d0e}a{color:#93c5fd}@media(max-width:700px){.req{grid-template-columns:1fr}}
</style></head><body><main><div class="head"><div><h1>Aegis.Configuration</h1><div class="muted">Fleet Commissioning & Drift Control</div></div><div><a href="/api/commissioning">API</a> · <a href="/api/history">History</a></div></div>{{cards}}</main></body></html>
""", "text/html");
});

app.MapGet("/api/contracts", async (IConfigurationContractStore store, CancellationToken ct) => Results.Ok(await store.LoadAllAsync(ct)));
app.MapGet("/api/commissioning", async (CommissioningService service, CancellationToken ct) => Results.Ok(await service.EvaluateAllAsync(ct)));
app.MapGet("/api/history", async (ICommissioningHistoryStore history, CancellationToken ct) => Results.Ok(await history.ReadAsync(null, ct)));
app.MapGet("/api/history/{applicationId}", async (string applicationId, ICommissioningHistoryStore history, CancellationToken ct) => Results.Ok(await history.ReadAsync(applicationId, ct)));
app.MapPost("/api/commission/{applicationId}", async (string applicationId, CommissioningService service, CommissioningRecorder recorder, CancellationToken ct) =>
{
    CommissioningSnapshot? snapshot = (await service.EvaluateAllAsync(ct)).FirstOrDefault(x => string.Equals(x.ApplicationId, applicationId, StringComparison.OrdinalIgnoreCase));
    return snapshot is null ? Results.NotFound() : Results.Ok(await recorder.RecordAsync(snapshot, ct));
});
app.MapGet("/api/report/{applicationId}", async (string applicationId, CommissioningService service, InstructionPackCatalog packs, CommissioningReportRenderer renderer, CancellationToken ct) =>
{
    CommissioningSnapshot? snapshot = (await service.EvaluateAllAsync(ct)).FirstOrDefault(x => string.Equals(x.ApplicationId, applicationId, StringComparison.OrdinalIgnoreCase));
    if (snapshot is null) return Results.NotFound();
    ConfigurationInstructionPack? pack = await packs.FindAsync(snapshot.ApplicationId, snapshot.ApplicationVersion, ct);
    return Results.Text(renderer.RenderMarkdown(snapshot, pack), "text/markdown");
});
app.MapGet("/api/instructions/{applicationId}", async (string applicationId, CommissioningService service, InstructionPackCatalog packs, CancellationToken ct) =>
{
    CommissioningSnapshot? snapshot = (await service.EvaluateAllAsync(ct)).FirstOrDefault(x => string.Equals(x.ApplicationId, applicationId, StringComparison.OrdinalIgnoreCase));
    return snapshot is null ? Results.NotFound() : Results.Ok(await packs.FindAsync(snapshot.ApplicationId, snapshot.ApplicationVersion, ct));
});
app.MapGet("/api/drift/{applicationId}", async (string applicationId, CommissioningService service, ICommissioningHistoryStore history, DriftDetector detector, CancellationToken ct) =>
{
    CommissioningSnapshot? current = (await service.EvaluateAllAsync(ct)).FirstOrDefault(x => string.Equals(x.ApplicationId, applicationId, StringComparison.OrdinalIgnoreCase));
    CommissioningHistoryEntry? previous = (await history.ReadAsync(applicationId, ct)).FirstOrDefault();
    if (current is null) return Results.NotFound();
    return previous is null ? Results.Ok(new { applicationId, status = "No baseline recorded" }) : Results.Ok(detector.Compare(previous, current));
});
app.MapPost("/api/contracts/validate", (ApplicationConfigurationContract contract, ContractValidator validator) => Results.Ok(validator.Validate(contract)));
app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "Aegis.Configuration" }));
app.Run();

static string RenderApplication(CommissioningSnapshot snapshot, ConfigurationReadinessScore score)
{
    HtmlEncoder e = HtmlEncoder.Default;
    string requirements = string.Join("", snapshot.Evaluation.Requirements.Select(r => $"<div class='req'><strong>{e.Encode(r.DisplayName)}</strong><div><span class='pill {r.State}'>{r.State}</span></div><div class='muted'>{e.Encode(r.Message)}</div></div>"));
    string id = Uri.EscapeDataString(snapshot.ApplicationId);
    return $"<section class='card'><div class='head'><div><h2>{e.Encode(snapshot.ApplicationId)} <span class='muted'>v{e.Encode(snapshot.ApplicationVersion)}</span></h2><div>{(snapshot.Evaluation.IsOperational ? "Verified / Operational" : "Commissioning required")}</div></div><div class='score {score.Grade}'>{score.Score}%</div></div><p><a href='/api/instructions/{id}'>Instructions</a> · <a href='/api/report/{id}'>Report</a> · <a href='/api/drift/{id}'>Drift</a></p>{requirements}</section>";
}
