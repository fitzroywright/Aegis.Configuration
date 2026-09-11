using System.Text.Encodings.Web;
using Aegis.Configuration.Application;
using Aegis.Configuration.Domain;
using Aegis.Configuration.Infrastructure;
using Common.Secrets;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

string contractDirectory = builder.Configuration["AegisConfiguration:ContractDirectory"]
    ?? Path.Combine(builder.Environment.ContentRootPath, "contracts");

builder.Services.AddCommonSecrets(builder.Configuration);
builder.Services.AddCommonSecretsDiagnostics();
builder.Services.AddSingleton<IConfigurationContractStore>(new DirectoryContractStore(contractDirectory));
builder.Services.AddSingleton<ISecretValueResolver, CommonSecretsValueResolver>();
builder.Services.AddSingleton<IConfigurationValueResolver, RuntimeConfigurationValueResolver>();
builder.Services.AddSingleton<IConfigurationRequirementValidator, UriRequirementValidator>();
builder.Services.AddHttpClient<HttpEndpointRequirementValidator>();
builder.Services.AddSingleton<IConfigurationRequirementValidator>(provider => provider.GetRequiredService<HttpEndpointRequirementValidator>());
builder.Services.AddSingleton<IConfigurationRequirementValidator, TcpEndpointRequirementValidator>();
builder.Services.AddSingleton<IConfigurationRequirementValidator, PostgreSqlRequirementValidator>();
builder.Services.AddSingleton<ConfigurationEvaluator>();
builder.Services.AddSingleton<CommissioningService>();

WebApplication app = builder.Build();

app.MapGet("/", async (CommissioningService commissioning, CancellationToken cancellationToken) =>
{
    IReadOnlyList<CommissioningSnapshot> snapshots = await commissioning.EvaluateAllAsync(cancellationToken);
    string cards = string.Join("", snapshots.Select(RenderApplication));
    if (cards.Length == 0)
    {
        cards = "<section class='card'><h2>No application contracts discovered</h2><p>Place *.configuration-contract.json files in the configured contracts directory.</p></section>";
    }

    return Results.Content($$"""
<!doctype html><html><head><title>Aegis.Configuration</title><meta name="viewport" content="width=device-width,initial-scale=1"><style>
body{font-family:system-ui;margin:0;background:#0b1220;color:#e5e7eb}main{max-width:1180px;margin:auto;padding:32px}.head{display:flex;justify-content:space-between;align-items:end;gap:20px}.muted{color:#94a3b8}.card{background:#111827;border:1px solid #334155;border-radius:12px;padding:20px;margin:16px 0}.req{display:grid;grid-template-columns:1.4fr .7fr 2fr;gap:14px;padding:12px 0;border-top:1px solid #263244}.pill{display:inline-block;padding:4px 9px;border-radius:999px;background:#334155}.Verified{background:#14532d}.Missing{background:#7f1d1d}.Invalid{background:#9a3412}.ConfiguredUnverified{background:#854d0e}.NotRequired{background:#334155}code{color:#93c5fd}@media(max-width:700px){.req{grid-template-columns:1fr}.head{display:block}}
</style></head><body><main><div class="head"><div><h1>Aegis.Configuration</h1><div class="muted">Commissioning Control Plane</div></div><div><a href="/api/commissioning" style="color:#93c5fd">JSON status</a></div></div>{{cards}}</main></body></html>
""", "text/html");
});

app.MapGet("/api/contracts", async (IConfigurationContractStore store, CancellationToken cancellationToken) =>
    Results.Ok(await store.LoadAllAsync(cancellationToken)));

app.MapGet("/api/commissioning", async (CommissioningService commissioning, CancellationToken cancellationToken) =>
    Results.Ok(await commissioning.EvaluateAllAsync(cancellationToken)));

app.MapPost("/api/contracts/evaluate", async (
    ApplicationConfigurationContract contract,
    ConfigurationEvaluator evaluator,
    CancellationToken cancellationToken) => Results.Ok(await evaluator.EvaluateAsync(contract, cancellationToken)));

app.MapPost("/api/contracts/inspect", async (HttpRequest request, CancellationToken cancellationToken) =>
{
    JsonContractStore store = new();
    ApplicationConfigurationContract contract = await store.LoadAsync(request.Body, cancellationToken);
    return Results.Ok(contract);
});

app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "Aegis.Configuration" }));
app.Run();

static string RenderApplication(CommissioningSnapshot snapshot)
{
    HtmlEncoder encoder = HtmlEncoder.Default;
    string requirements = string.Join("", snapshot.Evaluation.Requirements.Select(requirement =>
        $"<div class='req'><strong>{encoder.Encode(requirement.DisplayName)}</strong><div><span class='pill {requirement.State}'>{requirement.State}</span></div><div class='muted'>{encoder.Encode(requirement.Message)}</div></div>"));
    string operational = snapshot.Evaluation.IsOperational ? "Verified / Operational" : "Commissioning required";
    return $"<section class='card'><h2>{encoder.Encode(snapshot.ApplicationId)} <span class='muted'>v{encoder.Encode(snapshot.ApplicationVersion)}</span></h2><p>{operational}</p>{requirements}</section>";
}
