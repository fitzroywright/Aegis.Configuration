using Aegis.Configuration.Domain;
using Aegis.Configuration.Infrastructure;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
WebApplication app = builder.Build();

app.MapGet("/", () => Results.Content("""
<!doctype html>
<html><head><title>Aegis.Configuration</title><meta name="viewport" content="width=device-width,initial-scale=1"><style>
body{font-family:system-ui;margin:0;background:#111827;color:#e5e7eb}main{max-width:1000px;margin:auto;padding:32px}h1{margin-bottom:4px}.card{background:#1f2937;border:1px solid #374151;border-radius:10px;padding:20px;margin:16px 0}.muted{color:#9ca3af}.states{display:flex;gap:8px;flex-wrap:wrap}.state{padding:8px 12px;border-radius:6px;background:#374151}code{color:#93c5fd}</style></head>
<body><main><h1>Aegis.Configuration</h1><div class="muted">Application Commissioning Control Plane</div>
<div class="card"><h2>Configuration lifecycle</h2><div class="states"><span class="state">Missing</span><span class="state">Invalid</span><span class="state">Configured / Unverified</span><span class="state">Verified</span></div></div>
<div class="card"><h2>First vertical slice</h2><p>Submit an application configuration contract to <code>POST /api/contracts/evaluate</code>. The service validates the contract and returns its commissioning requirements. Runtime value resolution and Common.Secrets/Common.Diagnostics adapters are the next integration layer.</p></div>
</main></body></html>
""", "text/html"));

app.MapPost("/api/contracts/inspect", async (HttpRequest request, CancellationToken cancellationToken) =>
{
    JsonContractStore store = new();
    ApplicationConfigurationContract contract = await store.LoadAsync(request.Body, cancellationToken);
    return Results.Ok(contract);
});

app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "Aegis.Configuration" }));

app.Run();
