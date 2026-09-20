using Microsoft.AspNetCore.HttpOverrides;
using Npgsql;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Aegis.Configuration.Application;
using Aegis.Configuration.Domain;
using Aegis.Configuration.Infrastructure;
using Common.Diagnostics;
using Common.Registration;
using Common.Secrets;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
string operationsLogInstanceId = builder.Configuration["Aegis:Registration:InstanceId"]?.Trim() ?? Environment.MachineName;
string operationsLogCredentialFile = builder.Configuration["Aegis:Registration:CredentialFile"]?.Trim()
    ?? "/var/lib/aegis/configuration/registration.key";
builder.Services.AddAegisControlPlaneOperationsLogging(builder.Configuration, "Aegis.Configuration", operationsLogInstanceId, operationsLogCredentialFile, builder.Environment.EnvironmentName);
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("registration-lifecycle-rate-limit", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 30,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
});
string contractDirectory = builder.Configuration["AegisConfiguration:ContractDirectory"] ?? "/var/lib/aegis/configuration/contracts";
string runbookDirectory = builder.Configuration["AegisConfiguration:RunbookDirectory"] ?? "/var/lib/aegis/configuration/runbooks";
string instructionDirectory = builder.Configuration["AegisConfiguration:InstructionDirectory"] ?? Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "..", "..", "instruction-packs"));
builder.Services.AddCommonSecrets(builder.Configuration);
builder.Services.AddSuiteSecurity();
builder.Services.AddHttpClient("operations-registration", client => client.Timeout = TimeSpan.FromSeconds(10));
builder.Services.AddHttpClient("registration-lifecycle-telemetry", client => client.Timeout = TimeSpan.FromSeconds(5));
builder.Services.AddSingleton<RegistrationLifecycleTelemetryPublisher>();
builder.Services.AddSingleton<OperationsRegistrationAuthority>();
builder.Services.AddSingleton<ILifecycleRegistrationStore, PostgresLifecycleRegistrationStore>();
builder.Services.AddSingleton<LifecycleRegistrationAuthority>();
builder.Services.AddSingleton<RegistrationReconciliationState>();
builder.Services.AddHostedService<LifecycleRegistrationReconciliationService>();
builder.Services.AddHostedService<ConfigurationRegistrationPublisher>();
DirectoryContractStore contractStore = new(contractDirectory);
builder.Services.AddSingleton(contractStore);
builder.Services.AddSingleton<IConfigurationContractStore>(contractStore);
builder.Services.AddSingleton<IConfigurationContractRegistrationStore>(contractStore);
builder.Services.AddSingleton(new InstructionPackCatalog(instructionDirectory));
builder.Services.AddSingleton<ConfigurationEvaluator>();
builder.Services.AddSingleton<CommissioningService>();
builder.Services.AddSingleton<CommissioningReportRenderer>();
builder.Services.AddSingleton(new RunbookArtifactStore(runbookDirectory));
builder.Services.AddSingleton<ContractValidator>();
WebApplication app = builder.Build();
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});
app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (NpgsqlException exception)
        when (context.Request.Path.StartsWithSegments("/api/registration"))
    {
        ILogger logger = context.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("Aegis.Configuration.RegistrationDatabase");

        logger.LogError(
            exception,
            "Registration authority database is unavailable for {Method} {Path}.",
            context.Request.Method,
            context.Request.Path);

        if (!context.Response.HasStarted)
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            context.Response.ContentType = "application/problem+json";
            context.Response.Headers.RetryAfter = "10";
            await context.Response.WriteAsJsonAsync(new
            {
                type = "https://ffpja.org/problems/registration-authority-unavailable",
                title = "Registration Authority Unavailable",
                status = StatusCodes.Status503ServiceUnavailable,
                detail = "The registration database is temporarily unavailable. No registration state change was accepted.",
                traceId = context.TraceIdentifier
            });
        }
    }
    catch (Exception exception)
        when (context.Request.Path.StartsWithSegments("/api/registration"))
    {
        ILogger logger = context.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("Aegis.Configuration.Registration");

        logger.LogError(
            exception,
            "Unexpected registration lifecycle failure for {Method} {Path}. TraceId={TraceId}.",
            context.Request.Method,
            context.Request.Path,
            context.TraceIdentifier);

        if (!context.Response.HasStarted)
        {
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/problem+json";
            await context.Response.WriteAsJsonAsync(new
            {
                type = "https://ffpja.org/problems/registration-action-failed",
                title = "Registration action failed",
                status = StatusCodes.Status500InternalServerError,
                detail = "An unexpected registration lifecycle error occurred. No success was reported. Check the Configuration service log using the trace ID.",
                traceId = context.TraceIdentifier
            });
        }
    }
});
app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (Exception ex) when (
        context.Request.Path.StartsWithSegments("/api/registration") &&
        ex is NpgsqlException or TimeoutException)
    {
        app.Logger.LogError(
            ex,
            "Registration authority persistence is unavailable for {Path}.",
            context.Request.Path);

        if (!context.Response.HasStarted)
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            context.Response.ContentType = "application/problem+json";
            await context.Response.WriteAsJsonAsync(new
            {
                type = "https://ffpja.org/problems/registration-authority-unavailable",
                title = "Registration authority unavailable",
                status = StatusCodes.Status503ServiceUnavailable,
                detail = "Registration persistence is temporarily unavailable. No registration state change was reported as successful."
            });
        }
    }
});
app.UseRateLimiter();
app.UseSuiteSecurity();
app.MapSuiteSecurity();
app.MapLifecycleRegistration();
app.MapGet("/api/registration/summary", async (HttpContext context, LifecycleRegistrationAuthority authority, CancellationToken ct) =>
{
    if (!Has(context, "Configuration.View")) return Forbidden();

    IReadOnlyList<LifecycleRegistrationRecord> records = await authority.ListAsync(ct);
    return Results.Ok(new
    {
        total = records.Count,
        pending = records.Count(x => x.State is LifecycleRegistrationState.Pending or LifecycleRegistrationState.Approved),
        registered = records.Count(x => x.State == LifecycleRegistrationState.Registered),
        recoveryPending = records.Count(x => x.State is LifecycleRegistrationState.RecoveryPending or LifecycleRegistrationState.RecoveryApproved),
        conflicts = records.Count(x => x.State == LifecycleRegistrationState.IdentityConflict),
        revoked = records.Count(x => x.State == LifecycleRegistrationState.Revoked),
        rejected = records.Count(x => x.State == LifecycleRegistrationState.Rejected),
        attention = records.Count(x => x.State is
            LifecycleRegistrationState.RecoveryPending or
            LifecycleRegistrationState.RecoveryApproved or
            LifecycleRegistrationState.IdentityConflict or
            LifecycleRegistrationState.Revoked or
            LifecycleRegistrationState.Rejected),
        checkedAtUtc = DateTimeOffset.UtcNow
    });
});

app.MapGet("/api/registration/flow", async (
    HttpContext context,
    int? limit,
    LifecycleRegistrationAuthority authority,
    CancellationToken ct) =>
{
    if (!Has(context, "Configuration.View")) return Forbidden();

    IReadOnlyList<LifecycleRegistrationRecord> records =
        await authority.ListAllAsync(ct);

    return Results.Ok(
        LifecycleFlowSnapshotBuilder.Build(
            records,
            limit ?? 150));
});

app.MapGet("/api/registration/reconciliation", (HttpContext context, RegistrationReconciliationState state) =>
{
    if (!Has(context, "Configuration.Manage")) return Forbidden();
    return Results.Ok(state.Get());
});

app.MapGet("/", async (IConfigurationContractStore store, ConfigurationEvaluator evaluator, CancellationToken ct) =>
{
    var contracts = (await store.LoadAllAsync(ct)).OrderBy(x => x.DisplayName).ToList();
    var rows = new List<(ApplicationConfigurationContract Contract, ConfigurationEvaluation Evaluation)>();
    foreach (var c in contracts) rows.Add((c, await evaluator.EvaluateAsync(c, ct)));
    int orphanedContracts = await contractStore.CountRetiredAsync("orphaned", ct);
    return Html(RenderDashboard(rows, null, orphanedContracts));
});

app.MapGet("/applications/{applicationId}", async (string applicationId, IConfigurationContractStore store, ConfigurationEvaluator evaluator, CancellationToken ct) =>
{
    var contracts = (await store.LoadAllAsync(ct)).OrderBy(x => x.DisplayName).ToList();
    var rows = new List<(ApplicationConfigurationContract Contract, ConfigurationEvaluation Evaluation)>();
    foreach (var c in contracts) rows.Add((c, await evaluator.EvaluateAsync(c, ct)));
    int selected = rows.FindIndex(x => string.Equals(x.Contract.ApplicationId, applicationId, StringComparison.OrdinalIgnoreCase));
    int orphanedContracts = await contractStore.CountRetiredAsync("orphaned", ct);
    return selected < 0 ? Results.NotFound() : Html(RenderDashboard(rows, selected, orphanedContracts));
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

app.MapPost("/api/contracts/register", async (HttpContext context, ApplicationConfigurationContract contract, IConfigurationContractRegistrationStore store, ContractValidator validator, OperationsRegistrationAuthority operationsAuthority, LifecycleRegistrationAuthority lifecycleAuthority, ConfigurationEvaluator evaluator, CommissioningReportRenderer renderer, RunbookArtifactStore runbooks, ILoggerFactory loggerFactory, CancellationToken ct) =>
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

    string installationId = context.Request.Headers["X-Aegis-Installation-Id"].ToString().Trim();
    if (!string.IsNullOrWhiteSpace(installationId))
    {
        string applicationId = context.Request.Headers["X-Aegis-Application-Id"].ToString().Trim();
        string instanceId = context.Request.Headers["X-Aegis-Instance-Id"].ToString().Trim();
        string credential = string.Empty;
        if (System.Net.Http.Headers.AuthenticationHeaderValue.TryParse(
                context.Request.Headers.Authorization.ToString(),
                out var authorization) &&
            string.Equals(authorization.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase))
        {
            credential = authorization.Parameter?.Trim() ?? string.Empty;
        }

        if (string.IsNullOrWhiteSpace(applicationId) ||
            string.IsNullOrWhiteSpace(instanceId) ||
            string.IsNullOrWhiteSpace(credential) ||
            !string.Equals(contract.ApplicationId, applicationId, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(contract.InstanceId, instanceId, StringComparison.OrdinalIgnoreCase))
        {
            registrationLogger.LogWarning("Authenticated application contract identity did not match the submitted contract.");
            return Results.Unauthorized();
        }

        LifecycleRegistrationRecord? authenticated = await lifecycleAuthority.AuthenticateAsync(
            applicationId,
            instanceId,
            installationId,
            credential,
            attemptId,
            ct);

        if (authenticated is null || authenticated.State != LifecycleRegistrationState.Registered)
        {
            registrationLogger.LogWarning("Application durable identity could not authenticate contract publication.");
            return Results.Unauthorized();
        }

        registrationLogger.LogInformation(
            "Application durable identity authenticated for configuration contract publication.");
    }
    else
    {
        if (!await operationsAuthority.ValidateOperationsAsync(context.Request, ct))
        {
            registrationLogger.LogWarning("Configuration contract registration rejected because neither durable application identity nor Operations control-plane identity authenticated.");
            return Results.Unauthorized();
        }

        registrationLogger.LogInformation("Operations control-plane identity authenticated for contract publication.");
    }
    var validation = validator.Validate(contract);
    if (!validation.IsValid)
    {
        registrationLogger.LogWarning("Configuration contract validation failed.");

        if (!string.IsNullOrWhiteSpace(installationId))
        {
            await lifecycleAuthority.RecordOperationalEventAsync(
                contract.ApplicationId,
                contract.InstanceId!,
                installationId,
                "ConfigurationContractRejected",
                "Configuration contract validation failed.",
                attemptId,
                ct);
        }

        return Results.BadRequest(validation);
    }
    ConfigurationContractRegistrationResult registration = await store.RegisterAsync(contract, ct);
    registrationLogger.LogInformation("Configuration contract registration succeeded. Changed={Changed}.", registration.Changed);

    ApplicationConfigurationContract registeredContract = contract with
    {
        LastRegisteredAtUtc = registration.LastRegisteredAtUtc
    };
    ConfigurationEvaluation runbookEvaluation = await evaluator.EvaluateAsync(registeredContract, ct);
    DateTimeOffset runbookGeneratedAtUtc = DateTimeOffset.UtcNow;
    string runbookMarkdown = renderer.RenderOperationalRunbook(
        registeredContract,
        runbookEvaluation,
        runbookGeneratedAtUtc);
    RunbookArtifact runbook = await runbooks.SaveAsync(
        contract.ApplicationId,
        runbookMarkdown,
        ct);
    registrationLogger.LogInformation(
        "Operational runbook generated for {ApplicationId}. File={RunbookFile}.",
        contract.ApplicationId,
        runbook.FileName);

    if (!string.IsNullOrWhiteSpace(installationId))
    {
        await lifecycleAuthority.RecordOperationalEventAsync(
            contract.ApplicationId,
            contract.InstanceId!,
                installationId,
            "ConfigurationContractPublished",
            registration.Changed ? "Configuration contract accepted with changes." : "Configuration contract accepted with no material changes.",
            attemptId,
            ct);

        await lifecycleAuthority.RecordOperationalEventAsync(
            contract.ApplicationId,
            contract.InstanceId!,
            installationId,
            "RunbookGenerated",
            $"Operational runbook generated at {runbookGeneratedAtUtc:O}.",
            attemptId,
            ct);
    }

    return Results.Ok(new
    {
        registered = true,
        registration.ApplicationId,
        registration.Version,
        registration.Changed,
        registration.LastRegisteredAtUtc,
        runbook = $"/runbooks/{Uri.EscapeDataString(contract.ApplicationId)}",
        runbookGeneratedAtUtc
    });
});

app.MapPost("/api/contracts/{applicationId}/retire", async (HttpContext context, string applicationId, DirectoryContractStore store, OperationsRegistrationAuthority authority, CancellationToken ct) =>
{
    if (!await authority.ValidateOperationsAsync(context.Request, ct)) return Results.Unauthorized();
    string reason = context.Request.Query["reason"].ToString();
    bool retired = await store.RetireAsync(applicationId, string.IsNullOrWhiteSpace(reason) ? "revoked" : reason, ct);
    return retired ? Results.Ok(new { applicationId, retired = true }) : Results.NotFound();
});

app.MapDelete("/api/contracts/{applicationId}", async (HttpContext context, string applicationId, DirectoryContractStore store, OperationsRegistrationAuthority authority, CancellationToken ct) =>
{
    if (!await authority.ValidateOperationsAsync(context.Request, ct)) return Results.Unauthorized();
    bool purged = await store.PurgeAsync(applicationId, ct);
    return purged ? Results.NoContent() : Results.NotFound();
});

app.MapPost("/api/contracts/reconcile", async (
    HttpContext context,
    string[] activeApplicationIds,
    DirectoryContractStore store,
    OperationsRegistrationAuthority authority,
    LifecycleRegistrationAuthority lifecycleAuthority,
    CancellationToken ct) =>
{
    if (!await authority.ValidateOperationsAsync(context.Request, ct)) return Results.Unauthorized();

    HashSet<string> authoritativeActive = activeApplicationIds
        .Where(x => !string.IsNullOrWhiteSpace(x))
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    IReadOnlyList<LifecycleRegistrationRecord> lifecycleRegistrations =
        await lifecycleAuthority.ListAsync(ct);

    foreach (LifecycleRegistrationRecord registration in lifecycleRegistrations)
    {
        if (registration.State == LifecycleRegistrationState.Registered)
            authoritativeActive.Add(registration.ApplicationId);
    }

    IReadOnlyList<string> retired = await store.ReconcileAsync(
        authoritativeActive.ToArray(),
        ct);

    return Results.Ok(new
    {
        active = authoritativeActive.Count,
        controlPlaneActive = activeApplicationIds.Length,
        lifecycleActive = lifecycleRegistrations.Count(x => x.State == LifecycleRegistrationState.Registered),
        retired
    });
});

app.MapGet("/api/contracts/discovery", async (HttpContext context, IConfigurationContractStore store, OperationsRegistrationAuthority authority, CancellationToken ct) =>
{
    if (!await authority.ValidateOperationsAsync(context.Request, ct)) return Results.Unauthorized();
    IReadOnlyList<ApplicationConfigurationContract> contracts = await store.LoadAllAsync(ct);
    context.Response.Headers.CacheControl = "no-store";
    return Results.Ok(contracts);
});

app.MapGet("/contracts", async (
    HttpContext context,
    IConfigurationContractStore store,
    ConfigurationEvaluator evaluator,
    CancellationToken ct) =>
{
    if (!Has(context, "Configuration.View")) return Forbidden();

    IReadOnlyList<ApplicationConfigurationContract> contracts = await store.LoadAllAsync(ct);
    var rows = new List<(ApplicationConfigurationContract Contract, ConfigurationEvaluation Evaluation)>();
    foreach (ApplicationConfigurationContract contract in contracts.OrderBy(x => x.DisplayName))
        rows.Add((contract, await evaluator.EvaluateAsync(contract, ct)));

    string cards = rows.Count == 0
        ? "<div class='empty'>No application contracts have been published yet.</div>"
        : string.Join("", rows.Select(x =>
        {
            string appId = Uri.EscapeDataString(x.Contract.ApplicationId);
            int required = x.Contract.Requirements.Count(r => r.Required);
            int optional = x.Contract.Requirements.Count(r => !r.Required);
            int secrets = x.Contract.Requirements.Count(r => r.Kind == ConfigurationRequirementKind.Secret);
            string statusClass = x.Evaluation.IsComplete ? "good" : "warn";
            string statusText = x.Evaluation.IsComplete ? "Configured" : "Needs Attention";
            string registered = Registered(x.Contract.LastRegisteredAtUtc, "MMM d, yyyy HH:mm");
            return $"<article class='contract-card panel'>" +
                   $"<div class='contract-card-head'>{AppIcon(x.Contract, 1, "bigicon")}<div><h2>{E(x.Contract.DisplayName)}</h2><small>{E(x.Contract.ApplicationId)}</small></div><span class='status {statusClass}'>{statusText}</span></div>" +
                   $"<p class='muted'>{E(x.Contract.Description)}</p>" +
                   $"<div class='facts contract-facts'>" +
                   $"<div><small>VERSION</small><b>{E(x.Contract.Version)}</b></div>" +
                   $"<div><small>INSTANCE</small><b>{E(x.Contract.InstanceId)}</b></div>" +
                   $"<div><small>REQUIRED</small><b>{required}</b></div>" +
                   $"<div><small>OPTIONAL</small><b>{optional}</b></div>" +
                   $"<div><small>SECRETS</small><b>{secrets}</b></div>" +
                   $"<div><small>LAST PUBLISHED</small><b>{registered}</b></div>" +
                   $"</div>" +
                   $"<div class='contract-actions'>" +
                   $"<a class='primary' href='/applications/{appId}/contract'>Open Contract</a>" +
                   $"<a href='/applications/{appId}'>Requirements</a>" +
                   $"<a href='/runbooks/{appId}'>Runbook</a>" +
                   $"</div></article>";
        }));

    string content =
        "<style>.contracts-grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(420px,1fr));gap:14px;margin-top:14px}.contract-card{padding:16px;min-height:0}.contract-card-head{display:flex;align-items:center;gap:12px;border-bottom:1px solid #24516c;padding-bottom:12px}.contract-card-head h2{margin:0}.contract-card-head .status{margin-left:auto}.contract-facts{grid-template-columns:repeat(3,minmax(0,1fr))}.contract-actions{display:flex;gap:8px;flex-wrap:wrap;margin-top:14px}.contract-actions a{border:1px solid #22638d;padding:9px 12px;border-radius:5px}.contract-actions .primary{background:#087fff;font-weight:800}@media(max-width:700px){.contracts-grid{grid-template-columns:1fr}.contract-facts{grid-template-columns:1fr 1fr}}</style>" +
        "" +
        "<section class='toolbar'><input id='contractSearch' placeholder='⌕  Search contracts...'><a href='/contracts/raw' class='primary'>Raw Contract API</a></section>" +
        "<section id='contractCards' class='contracts-grid'>" + cards + "</section>" +
        "<script>const cs=document.getElementById('contractSearch');if(cs)cs.addEventListener('input',()=>{const q=cs.value.toLowerCase();document.querySelectorAll('#contractCards .contract-card').forEach(x=>x.style.display=x.innerText.toLowerCase().includes(q)?'':'none');});</script>";

    return Html(ConfigurationShell.Render("Application Contracts", content, "contracts"));
});

app.MapGet("/contracts/raw", async (
    HttpContext context,
    IConfigurationContractStore store,
    CancellationToken ct) =>
{
    if (!Has(context, "Configuration.View")) return Forbidden();

    IReadOnlyList<ApplicationConfigurationContract> contracts = await store.LoadAllAsync(ct);
    string json = JsonSerializer.Serialize(contracts, new JsonSerializerOptions
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    });
    string encoded = HtmlEncoder.Default.Encode(json);
    string content =
        "<style>.raw-contract-toolbar{display:flex;justify-content:flex-end;gap:8px;margin-bottom:10px}.raw-contract-toolbar a,.raw-contract-toolbar button{border:1px solid var(--line);border-radius:8px;padding:8px 11px;background:#071722;color:var(--starship-white);font-weight:800;cursor:pointer}.raw-contract{margin:0;border:1px solid var(--line-soft);border-top:2px solid var(--line);border-radius:10px;background:#02080d;color:var(--arctic-snow);padding:16px;white-space:pre;overflow:auto;max-height:calc(100vh - 330px);font:12px/1.45 ui-monospace,SFMono-Regular,Consolas,monospace}</style>" +
        "<section class='raw-contract-toolbar'><button type='button' onclick='navigator.clipboard.writeText(document.getElementById(\"rawContract\").textContent)'>Copy JSON</button><a href='/api/contracts' target='_blank' rel='noopener'>Open API JSON</a></section>" +
        "<pre id='rawContract' class='raw-contract'>" + encoded + "</pre>";

    return Html(ConfigurationShell.Render("Raw Contract API", content, "contracts"));
});

app.MapGet("/api/contracts", async (IConfigurationContractStore store, CancellationToken ct) => Results.Ok(await store.LoadAllAsync(ct)));
app.MapGet("/registration-lifecycle", async (
    HttpContext context,
    LifecycleRegistrationAuthority authority,
    CancellationToken ct) =>
{
    if (!Has(context, "Configuration.View")) return Forbidden();

    IReadOnlyList<LifecycleRegistrationRecord> records = await authority.ListAllAsync(ct);
    int registered = records.Count(x => x.State == LifecycleRegistrationState.Registered);
    int pending = records.Count(x => x.State is LifecycleRegistrationState.Pending or LifecycleRegistrationState.Approved);
    int recovery = records.Count(x => x.State is LifecycleRegistrationState.RecoveryPending or LifecycleRegistrationState.RecoveryApproved);
    int attention = records.Count(x => x.State is LifecycleRegistrationState.IdentityConflict or LifecycleRegistrationState.Revoked or LifecycleRegistrationState.Rejected);

    string rows = records.Count == 0
        ? "<tr><td colspan='4' class='empty'>No registration lifecycle records are currently available.</td></tr>"
        : string.Join("", records.OrderBy(x => x.ApplicationId).Select(x =>
        {
            string id = Uri.EscapeDataString(x.RegistrationId);
            string actions = x.State switch
            {
                LifecycleRegistrationState.Pending or LifecycleRegistrationState.RecoveryPending =>
                    $"<button class='row-action approve' data-registration-action='approve' data-registration-id='{id}'>Approve</button>" +
                    $"<button class='row-action danger' data-registration-action='reject' data-registration-id='{id}'>Reject</button>",

                LifecycleRegistrationState.Registered =>
                    $"<button class='row-action danger' data-registration-action='revoke' data-registration-id='{id}'>Revoke</button>",

                LifecycleRegistrationState.Rejected or LifecycleRegistrationState.Revoked =>
                    $"<button class='row-action danger' data-registration-action='purge' data-registration-id='{id}'>Purge</button>",

                LifecycleRegistrationState.IdentityConflict =>
                    $"<button class='row-action approve' data-registration-action='conflict/retain-existing' data-registration-id='{id}'>Retain Existing</button>" +
                    $"<button class='row-action danger' data-registration-action='conflict/replace-with-incoming' data-registration-id='{id}'>Use Incoming</button>",

                _ => "<span class='muted'>No operator action</span>"
            };

            return $"<tr><td><div style='display:flex;align-items:center;gap:9px'>{RegistrationIcon(x)}<b>{E(x.ApplicationId)}</b></div></td><td>{E(x.InstanceId)}</td><td><span class='status {(x.State == LifecycleRegistrationState.Registered ? "good" : "warn")}'>{E(x.State.ToString())}</span></td><td><div class='row-actions'>{actions}</div></td></tr>";
        }));

    string content =
        "<section class='page-head'><div><p>Authoritative durable registration state for the control plane.</p></div></section>" +
        $"<section class='metrics'><a class='metric' href='/registration-lifecycle?state=Registered'><span>R</span><b>{registered}</b><small>Registered</small></a><a class='metric{(pending > 0 ? " issue-card" : "")}' href='/registration-lifecycle?state=Pending'><span>P</span><b>{pending}</b><small>Pending</small></a><a class='metric{(recovery > 0 ? " issue-card" : "")}' href='/registration-lifecycle?state=Recovery'><span>C</span><b>{recovery}</b><small>Recovery</small></a><a class='metric{(attention > 0 ? " issue-card" : "")}' href='/registration-lifecycle?state=Attention'><span>!</span><b>{attention}</b><small>Attention</small></a><a class='metric' href='/registration-lifecycle'><span>Σ</span><b>{records.Count}</b><small>Total</small></a></section>" +
        "<section class='panel table-panel'><table><thead><tr><th>Application</th><th>Instance</th><th>State</th><th>Actions</th></tr></thead><tbody>" + rows + "</tbody></table></section>";

    return Html(ConfigurationShell.Render("Registration Lifecycle", content, "registration"));
});

app.MapGet("/orphaned", async (
    HttpContext context,
    CancellationToken ct) =>
{
    if (!Has(context, "Configuration.View")) return Forbidden();

    IReadOnlyList<RetiredConfigurationContractInfo> orphaned =
        await contractStore.ListRetiredAsync("orphaned", ct);

    string rows = orphaned.Count == 0
        ? "<tr><td colspan='6' class='empty'>No orphaned contracts.</td></tr>"
        : string.Join("", orphaned.Select(x =>
            $"<tr><td><b>{E(x.DisplayName)}</b><small>{E(x.ApplicationId)}</small></td><td>{E(x.Version)}</td><td>{E(x.InstanceId)}</td><td>{Registered(x.LastRegisteredAtUtc, "MMM d, yyyy HH:mm")}</td><td><time class='local-datetime' datetime='{E(x.RetiredAtUtc.ToString("O"))}'>{E(x.RetiredAtUtc.ToString("u"))}</time></td><td>{E(x.Reason)}</td></tr>"));

    string content =
        "<section class='page-head'><div><p class='muted' style='font-size:12px;opacity:.72'>Contracts retired because no active registration exists for the application.</p></div></section>" +
        "<section class='panel orphan-help'><b>What to do</b><span>If the application still exists, start it and restore its registration so it can publish a fresh contract. If the application was intentionally removed, leave the orphaned contract retired as historical evidence; no action is required. Investigate entries you do not recognize before deleting any registration data.</span><a href='/registration-lifecycle'>Open Registration Lifecycle</a></section>" +
        "<section class='panel table-panel'><table><thead><tr><th>Application</th><th>Version</th><th>Instance</th><th>Last Registered</th><th>Retired</th><th>Reason</th></tr></thead><tbody>" + rows + "</tbody></table></section>";

    return Html(ConfigurationShell.Render("Orphaned Contracts", content, "orphaned"));
});

app.MapGet("/runbooks", async (
    HttpContext context,
    RunbookArtifactStore runbooks,
    CancellationToken ct) =>
{
    if (!Has(context, "Configuration.View")) return Forbidden();

    IReadOnlyList<RunbookArtifact> artifacts = await runbooks.ListAsync(ct);
    string rows = artifacts.Count == 0
        ? "<div class='empty'>No generated runbooks yet. A runbook is created automatically after a successful authenticated contract publication.</div>"
        : string.Join("", artifacts.Select(x =>
            $"<a class='req runbook-row' href='/runbooks/{Uri.EscapeDataString(x.ApplicationId)}'><span><b>{E(x.ApplicationId)}</b><small>Generated <time class='local-datetime' datetime='{E(x.LastUpdatedUtc.ToString("O"))}'>{E(x.LastUpdatedUtc.ToString("u"))}</time></small></span><span class='runbook-open'>Open Runbook</span><i class='ok'>✓</i></a>"));

    string content =
        "<section class='page-head'><div><p>Automatically generated operational artifacts from authenticated application contracts.</p></div></section>" +
        "<section class='panel'><div class='reqs'>" + rows + "</div></section>";

    return Html(ConfigurationShell.Render("Configuration Runbooks", content, "runbooks"));
});

app.MapGet("/runbooks/{applicationId}", async (
    HttpContext context,
    string applicationId,
    RunbookArtifactStore runbooks,
    CancellationToken ct) =>
{
    if (!Has(context, "Configuration.View")) return Forbidden();

    string? markdown = await runbooks.ReadAsync(applicationId, ct);
    if (markdown is null) return Results.NotFound();
    string content =
        "<section class='page-head'><div><p>Generated operational recovery and commissioning guidance.</p></div></section>" +
        "<section class='panel' style='padding:16px'><pre style='white-space:pre-wrap;overflow-wrap:anywhere;margin:0;font:13px/1.55 ui-monospace,SFMono-Regular,Consolas,monospace;color:var(--starship-white)'>" + E(markdown) + "</pre></section>";
    return Html(ConfigurationShell.Render(applicationId + " Runbook", content, "runbooks"));
});

app.MapGet("/api/report/{applicationId}", async (string applicationId, IConfigurationContractStore store, ConfigurationEvaluator evaluator, CommissioningReportRenderer renderer, CancellationToken ct) =>
{
    var contract = (await store.LoadAllAsync(ct)).FirstOrDefault(x => string.Equals(x.ApplicationId, applicationId, StringComparison.OrdinalIgnoreCase));
    if (contract is null) return Results.NotFound();
    ConfigurationEvaluation evaluation = await evaluator.EvaluateAsync(contract, ct);
    CommissioningSnapshot snapshot = new(contract.ApplicationId, contract.Version, DateTimeOffset.UtcNow, evaluation);
    return Results.Text(renderer.RenderMarkdown(snapshot), "text/markdown; charset=utf-8");
});
app.MapPost("/api/contracts/validate", (HttpContext context, ApplicationConfigurationContract contract, ContractValidator validator) => Has(context, "Configuration.Manage") ? Results.Ok(validator.Validate(contract)) : Forbidden());
app.MapAegisHealth("Aegis.Configuration");
app.Run();

static bool Has(HttpContext c, string permission) => c.Items.TryGetValue("SuiteIdentity", out object? value) && value is SuiteIdentity i && i.Permissions.Contains(permission, StringComparer.OrdinalIgnoreCase);
static IResult Forbidden() => Results.Json(new { error = "Forbidden", detail = "The authenticated SuiteSecurity session does not have the required permission." }, statusCode: StatusCodes.Status403Forbidden);
static IResult Html(string html) => Results.Content(html, "text/html; charset=utf-8");
static string E(string? value) => HtmlEncoder.Default.Encode(value ?? "—");
static string Registered(DateTimeOffset? value, string format) => value.HasValue
    ? $"<time class='local-datetime' datetime='{E(value.Value.ToString("O"))}'>{E(value.Value.ToString(format))}</time>"
    : "Never";

static string RenderDashboard(List<(ApplicationConfigurationContract Contract, ConfigurationEvaluation Evaluation)> rows, int? selectedIndex, int orphanedContracts = 0)
{
    int configured = rows.Count(x => x.Evaluation.IsComplete);
    int requirements = rows.Sum(x => x.Evaluation.Requirements.Count);
    string tableRows = rows.Count == 0 ? "<tr><td colspan='9' class='empty'>No applications registered.</td></tr>" : string.Join("", rows.Select((x, i) => RenderApplicationRow(x, i, selectedIndex == i)));
    int selected = selectedIndex ?? (rows.Count > 0 ? 0 : -1);
    string detail = selected < 0 ? "<section class='detail'><div class='empty'>Applications appear here automatically after they register with System Configuration.</div></section>" : RenderDetails(rows[selected]);
    string activity = rows.Count == 0 ? "<div class='empty'>No registration activity.</div>" : string.Join("", rows.OrderByDescending(x => x.Contract.LastRegisteredAtUtc).Take(4).Select(x => $"<div class='activity-row'><i></i><span><b>{E(x.Contract.DisplayName)}</b><small>Configuration contract registered</small></span><time>{Registered(x.Contract.LastRegisteredAtUtc, "MMM d, HH:mm")}</time></div>"));
    string content = $"<style>{SecretManagerStyles()}</style><section class='page-head'><div><p>All registered applications and their configuration status</p></div></section><section class='toolbar'><input id='search' placeholder='⌕  Search applications...'><select id='statusFilter'><option value=''>All Status</option><option>Configured</option><option>Not Configured</option></select><button onclick='location.reload()'>↻</button><button class='primary' onclick='document.getElementById(\"registrationInfoDialog\").showModal()'>ⓘ Application Registration</button></section><section class='metrics'><a class='metric' href='/'><span>▦</span><b>{rows.Count}</b><small>Total Applications</small></a><a class='metric green' href='/?status=Configured'><span>✓</span><b>{configured}</b><small>Configured</small></a><a class='metric{(rows.Count - configured > 0 ? " amber" : "")}' href='/?status=Not%20Configured'><span>!</span><b>{rows.Count - configured}</b><small>Not Configured</small></a><a class='metric{(orphanedContracts > 0 ? " amber" : "")}' href='/orphaned'><span>⚠</span><b>{orphanedContracts}</b><small>Orphaned Contracts</small></a><a class='metric' href='/contracts'><span>≡</span><b>{requirements}</b><small>Total Requirements</small></a></section><section class='workspace'><div><section class='panel table-panel'><table id='apps'><thead><tr><th>Application</th><th>Version</th><th>Site / Instance</th><th>Configuration</th><th>Secrets</th><th>Required</th><th>Optional</th><th>Last Updated</th><th></th></tr></thead><tbody>{tableRows}</tbody></table></section><section class='lower'><div class='panel'><h3>◴ Recent Activity</h3>{activity}</div><div class='panel'><h3>⌘ Quick Links</h3><div class='quick'><a href='/runbooks'>▤ <span><b>Configuration Runbooks</b><small>Commissioning and recovery guidance</small></span></a><a href='/contracts'>◇ <span><b>Application Contracts</b><small>Human-readable contract views</small></span></a><a href='/orphaned'>⚠ <span><b>Orphaned Contracts</b><small>Retired contracts with no active registration</small></span></a></div></div></section></div>{detail}</section><dialog id='registrationInfoDialog'><h2>Applications Register Automatically</h2><p>Applications publish their configuration requirements to System Configuration when they start. You do not manually add applications here.</p><p><b>Configuration Server</b><br><code id='configurationServerUrl'></code></p><p><b>Registration Endpoint</b><br><code id='registrationEndpointUrl'></code></p><p>Applications should normally configure <code>Aegis:Configuration:Url</code> with the Configuration Server address above.</p><p class='muted'>Normal applications authenticate directly with Configuration using their durable Common.Registration identity. Control-plane services use their restricted bootstrap identity. Secret values are never displayed here.</p><button onclick='this.closest(\"dialog\").close()'>Close</button></dialog>";
    return ConfigurationShell.Render("Applications", content, "applications");
}

static string RenderApplicationRow((ApplicationConfigurationContract Contract, ConfigurationEvaluation Evaluation) x, int index, bool selected)
{
    SecretManagerStatus secrets = SecretManagerEvaluator.Evaluate(x.Contract, x.Evaluation);
    string appId = Uri.EscapeDataString(x.Contract.ApplicationId);
    string secretSummary = secrets.HasSecrets ? $"<details class='secret-menu' onclick='event.stopPropagation()'><summary class='secret-pill {E(secrets.CssClass)}'><span>{E(secrets.Glyph)}</span>{E(secrets.ProviderName)} · {E(secrets.IssueLabel)}</summary><div class='secret-menu-items'><a href='/applications/{appId}/secrets'>Configure</a><a href='/applications/{appId}/secrets#issues'>View Issues</a>{(secrets.CanOpenProviderConsole ? $"<a href='{E(secrets.ManagementUrl)}' target='_blank' rel='noopener'>Open Provider Console</a>" : string.Empty)}</div></details>" : "<span class='secret-pill neutral'>— No secrets</span>";
    return $"<tr class='{(selected ? "selected" : "")}'><td><a href='/applications/{appId}'>{AppIcon(x.Contract, index)}<b>{E(x.Contract.DisplayName)}</b><small>{E(x.Contract.ApplicationId)}</small></a></td><td>{E(x.Contract.Version)}</td><td>{E(x.Contract.SiteId)}<small>{E(x.Contract.InstanceId)}</small></td><td><span class='status {(x.Evaluation.IsComplete ? "good" : "warn")}'>{(x.Evaluation.IsComplete ? "Configured" : "Not Configured")}</span></td><td>{secretSummary}</td><td>{x.Evaluation.Requirements.Count(r => r.Required)}</td><td>{x.Evaluation.Requirements.Count(r => !r.Required)}</td><td>{Registered(x.Contract.LastRegisteredAtUtc, "MMM d, HH:mm")}</td><td>›</td></tr>";
}

static string RenderDetails((ApplicationConfigurationContract Contract, ConfigurationEvaluation Evaluation) x)
{
    string req = string.Join("", x.Evaluation.Requirements.Where(r => r.Required).Select(RenderRequirement));
    string opt = string.Join("", x.Evaluation.Requirements.Where(r => !r.Required).Select(RenderRequirement));
    string environment = RenderRuntimeEnvironment(x.Contract.RuntimeEnvironment);
    string appId = Uri.EscapeDataString(x.Contract.ApplicationId);
    SecretManagerStatus secrets = SecretManagerEvaluator.Evaluate(x.Contract, x.Evaluation);
    string secretsSummary = secrets.HasSecrets ? $"<a class='secret-detail-link' href='/applications/{appId}/secrets'><span>{E(secrets.Glyph)}</span><span><b>Secrets: {E(secrets.ProviderName)}</b><small>{E(secrets.IssueLabel)}</small></span></a>" : "<div class='secret-detail-link disabled'><span>—</span><span><b>Secrets</b><small>No secret requirements declared</small></span></div>";
    return $"<aside class='detail'><div class='detail-head'>{AppIcon(x.Contract, 0, "bigicon")}<div><h2>{E(x.Contract.DisplayName)}</h2><small>{E(x.Contract.ApplicationId)}</small></div><span class='status {(x.Evaluation.IsComplete ? "good" : "warn")}'>{(x.Evaluation.IsComplete ? "Configured" : "Not Configured")}</span></div><div class='facts'><div><small>VERSION</small><b>{E(x.Contract.Version)}</b></div><div><small>SITE / INSTANCE</small><b>{E(x.Contract.SiteId)} / {E(x.Contract.InstanceId)}</b></div><div><small>REGISTERED</small><b>{Registered(x.Contract.LastRegisteredAtUtc, "MMM d, yyyy")}</b></div><div><small>APPLICATION ID</small><b>{E(x.Contract.ApplicationId)}</b></div></div>{secretsSummary}<nav class='tabs'><b>Requirements</b><a href='/applications/{appId}/details'>Details</a><a href='/applications/{appId}/details#diagnostics'>Diagnostics</a><a href='/runbooks/{appId}'>Runbooks</a><a href='/applications/{appId}/contract'>Contract</a></nav><h3>Required Requirements</h3><div class='reqs'>{(string.IsNullOrEmpty(req) ? "<div class='empty'>None</div>" : req)}</div><h3>Optional Requirements</h3><div class='reqs'>{(string.IsNullOrEmpty(opt) ? "<div class='empty'>None</div>" : opt)}</div>{environment}</aside>";
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
static string RegistrationIcon(LifecycleRegistrationRecord record)
{
    string? shortName = record.Metadata?["presentation"]?["shortName"]?.GetValue<string>();
    string? displayName = record.Metadata?["displayName"]?.GetValue<string>();
    string source = string.IsNullOrWhiteSpace(shortName)
        ? (string.IsNullOrWhiteSpace(displayName) ? record.ApplicationId : displayName!)
        : shortName!;
    string[] words = source.Replace("Aegis.", "", StringComparison.OrdinalIgnoreCase)
        .Replace("Aegis ", "", StringComparison.OrdinalIgnoreCase)
        .Split([' ', '.', '-', '_'], StringSplitOptions.RemoveEmptyEntries);
    string initials = words.Length switch
    {
        0 => "AE",
        1 => words[0][..Math.Min(2, words[0].Length)].ToUpperInvariant(),
        _ => string.Concat(words.Take(2).Select(x => char.ToUpperInvariant(x[0])))
    };

    string? icon = record.Metadata?["presentation"]?["iconUrl"]?.GetValue<string>()?.Trim();
    bool safe = !string.IsNullOrWhiteSpace(icon) &&
        ((icon.StartsWith("/", StringComparison.Ordinal) && !icon.StartsWith("//", StringComparison.Ordinal) && !icon.Contains("..", StringComparison.Ordinal)) ||
         (Uri.TryCreate(icon, UriKind.Absolute, out Uri? uri) && uri.Scheme == Uri.UriSchemeHttps));
    if (!safe)
        return $"<span class='appicon'>{E(initials)}</span>";

    return $"<span class='appicon appicon-image'><img src='{E(icon)}' alt='' loading='lazy' onerror=\"this.style.display='none';this.nextElementSibling.style.display='grid'\"><span class='appicon-fallback' style='display:none'>{E(initials)}</span></span>";
}

static string AppGlyph(int i) => new[] { "▣", "◇", "▦", "⌁", "◈", "▤" }[i % 6];
static string AppInitials(ApplicationConfigurationContract contract)
{
    string source = string.IsNullOrWhiteSpace(contract.Presentation?.ShortName)
        ? contract.DisplayName
        : contract.Presentation!.ShortName!;
    string[] words = source.Replace("Aegis.", "", StringComparison.OrdinalIgnoreCase)
        .Replace("Aegis ", "", StringComparison.OrdinalIgnoreCase)
        .Split([' ', '.', '-', '_'], StringSplitOptions.RemoveEmptyEntries);
    if (words.Length == 0) return "AE";
    if (words.Length == 1) return words[0][..Math.Min(2, words[0].Length)].ToUpperInvariant();
    return string.Concat(words.Take(2).Select(x => char.ToUpperInvariant(x[0])));
}
static string AppIcon(ApplicationConfigurationContract contract, int index, string cssClass = "appicon")
{
    string initials = E(AppInitials(contract));
    string? icon = contract.Presentation?.IconUrl?.Trim();
    if (string.IsNullOrWhiteSpace(icon))
        return $"<span class='{cssClass}'>{initials}</span>";

    bool safe = (icon.StartsWith("/", StringComparison.Ordinal) && !icon.StartsWith("//", StringComparison.Ordinal) && !icon.Contains("..", StringComparison.Ordinal)) ||
        (Uri.TryCreate(icon, UriKind.Absolute, out Uri? uri) && uri.Scheme == Uri.UriSchemeHttps);
    if (!safe) return $"<span class='{cssClass}'>{initials}</span>";

    return $"<span class='{cssClass} appicon-image'><img src='{E(icon)}' alt='' loading='lazy' onerror=\"this.style.display='none';this.nextElementSibling.style.display='grid'\"><span class='appicon-fallback' style='display:none'>{initials}</span></span>";
}
static string SecretManagerStyles() => ".secret-pill{display:inline-flex;align-items:center;gap:5px;border:1px solid;border-radius:5px;padding:5px 8px;font-weight:700;white-space:nowrap}.secret-pill.good{background:#00663f;border-color:#0c9e67}.secret-pill.warn{background:#6d4911;border-color:#c88a27}.secret-pill.neutral{background:#24394a;border-color:#5c7283}.secret-menu{position:relative}.secret-menu summary{cursor:pointer;list-style:none}.secret-menu summary::-webkit-details-marker{display:none}.secret-menu-items{position:absolute;z-index:20;right:0;min-width:190px;background:#001827;border:1px solid #2c7bab;border-radius:6px;padding:6px;box-shadow:0 8px 22px #0008}.secret-menu-items a{display:block;padding:8px;border-radius:4px;color:#ddecf7}.secret-menu-items a:hover{background:#07314a}.secret-detail-link{display:flex;align-items:center;gap:10px;margin:12px 0;padding:10px;border:1px solid #2c7bab;border-radius:6px;background:#062238;color:#e8f5ff}.secret-detail-link>span:first-child{font-size:20px}.secret-detail-link small{display:block;color:#9fb7c8}.secret-detail-link.disabled{opacity:.75}";