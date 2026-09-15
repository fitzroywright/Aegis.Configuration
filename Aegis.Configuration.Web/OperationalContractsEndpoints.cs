using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Aegis.Configuration.Application;
using Aegis.Configuration.Domain;
using Aegis.Configuration.Infrastructure;

internal static class OperationalContractsEndpoints
{
    public static void MapOperationalContracts(this WebApplication app)
    {
        app.MapPost("/api/registrations/register", async (
            HttpContext context,
            ApplicationOperationalRegistration registration,
            IConfigurationContractRegistrationStore configurationStore,
            DirectoryOperationalContractStore operationalStore,
            ContractValidator validator,
            IConfiguration configuration,
            CancellationToken ct) =>
        {
            string? configuredKey = configuration["AegisConfiguration:RegistrationKey"];
            if (string.IsNullOrWhiteSpace(configuredKey))
                return Results.Problem("Application registration is disabled because no registration credential is configured.", statusCode: StatusCodes.Status503ServiceUnavailable);

            string supplied = context.Request.Headers["X-Aegis-Registration-Key"].ToString();
            if (!FixedEquals(configuredKey, supplied)) return Results.Unauthorized();

            ContractValidationResult validation = validator.Validate(registration.Configuration);
            if (!validation.IsValid) return Results.BadRequest(validation);

            await configurationStore.RegisterAsync(registration.Configuration, ct);
            await operationalStore.RegisterAsync(registration, ct);
            return Results.Ok(new
            {
                registered = true,
                applicationId = registration.Configuration.ApplicationId,
                version = registration.Configuration.Version,
                contracts = new[] { "configuration", "dependencies", "environment" }
            });
        });

        app.MapGet("/api/dependencies", async (DirectoryOperationalContractStore store, CancellationToken ct)
            => Results.Ok(await store.LoadAllDependenciesAsync(ct)));
        app.MapGet("/api/environments", async (DirectoryOperationalContractStore store, CancellationToken ct)
            => Results.Ok(await store.LoadAllEnvironmentsAsync(ct)));

        app.MapGet("/applications/{applicationId}/operational", async (
            string applicationId,
            IConfigurationContractStore configurationStore,
            ConfigurationEvaluator evaluator,
            DirectoryOperationalContractStore operationalStore,
            CancellationToken ct) =>
        {
            ApplicationConfigurationContract? configurationContract = (await configurationStore.LoadAllAsync(ct))
                .FirstOrDefault(x => string.Equals(x.ApplicationId, applicationId, StringComparison.OrdinalIgnoreCase));
            if (configurationContract is null) return Results.NotFound();

            ConfigurationEvaluation evaluation = await evaluator.EvaluateAsync(configurationContract, ct);
            ApplicationDependencyContract? dependencies = await operationalStore.LoadDependenciesAsync(applicationId, ct);
            ApplicationEnvironmentContract? environment = await operationalStore.LoadEnvironmentAsync(applicationId, ct);
            return Results.Content(Render(configurationContract, evaluation, dependencies, environment), "text/html; charset=utf-8");
        });
    }

    private static string Render(
        ApplicationConfigurationContract configuration,
        ConfigurationEvaluation evaluation,
        ApplicationDependencyContract? dependencies,
        ApplicationEnvironmentContract? environment)
    {
        string configRows = string.Join("", evaluation.Requirements.Select(r => $"<div class='req'><span><b>{E(r.DisplayName)}</b><small>{E(r.Purpose)}{Condition(r)}</small></span><code>{DisplayValue(r)}</code><i class='{(r.State == ConfigurationState.Configured ? "ok" : "bad")}'>{(r.State == ConfigurationState.Configured ? "✓" : "!")}</i></div><div class='contract-meta'><small><b>Source:</b> {E(r.EffectiveSource)} &nbsp; <b>Priority:</b> {E(Join(r.ResolutionOrder))}</small></div>"));

        string dependencyRows = dependencies is null || dependencies.Dependencies.Count == 0
            ? "<div class='empty'>No dependency contract has been published.</div>"
            : string.Join("", dependencies.Dependencies.Select(d => $"<div class='req'><span><b>{E(d.DisplayName)}</b><small>{E(d.Purpose)}</small><small>{E(d.Required ? "Required" : "Optional")} · {E(d.Criticality)} · Interface: {E(d.Interface)}{Engine(d)}</small><small>Degraded: {E(d.DegradedBehavior)}</small></span><code>{E(d.Reachable is true ? "Reachable" : d.Reachable is false ? "Unreachable" : "Not tested")}</code><i class='{(d.Reachable is false && d.Required ? "bad" : "ok")}'>{(d.Reachable is false && d.Required ? "!" : "✓")}</i></div>"));

        string graph = dependencies is null || dependencies.Dependencies.Count == 0
            ? "<div class='empty'>Dependency graph appears after the application publishes dependencies.</div>"
            : RenderGraph(configuration, dependencies);

        string environmentRows = environment is null
            ? "<div class='empty'>No detected environment has been published.</div>"
            : RenderEnvironment(environment.Environment);

        string body = $"""
<section class='page-head'><div><h1>{E(configuration.DisplayName)}</h1><p>Configuration, dependencies and runtime-detected environment</p></div></section>
<section class='metrics'>
<div class='metric'><span>≡</span><b>{evaluation.Requirements.Count}</b><small>Configuration Items</small></div>
<div class='metric green'><span>⌁</span><b>{dependencies?.Dependencies.Count ?? 0}</b><small>Dependencies</small></div>
<div class='metric'><span>◈</span><b>{CountEnvironment(environment?.Environment)}</b><small>Detected Environment Facts</small></div>
<div class='metric amber'><span>!</span><b>{evaluation.Requirements.Count(r => r.State == ConfigurationState.Missing)}</b><small>Missing Required</small></div>
</section>
<section class='panel contract-section'><h2>Configuration Contract</h2><p class='muted'>The application declares every setting it actually uses, its allowed sources and resolution precedence. Secret values are never accepted for display.</p>{configRows}</section>
<section class='panel contract-section'><h2>Dependency Contract</h2><p class='muted'>Dependencies are explicit declarations from application code. Engines are not inferred from connection strings or ODBC.</p>{dependencyRows}</section>
<section class='panel contract-section'><h2>Dependency Graph</h2>{graph}</section>
<section class='panel contract-section'><h2>Detected Environment</h2><p class='muted'>Runtime observations only. No declared requirements, guesses or estimates.</p>{environmentRows}</section>
<div class='actions'><a href='/applications/{Uri.EscapeDataString(configuration.ApplicationId)}'>Back to Application</a><a href='/api/contracts'>Configuration API</a><a href='/api/dependencies'>Dependency API</a><a href='/api/environments'>Environment API</a></div>
<style>.contract-section{{padding:16px;margin:14px 0}}.contract-meta{{padding:0 9px 8px 9px;border-bottom:1px solid #143b55;color:#7fa7c2}}.depgraph{{display:flex;align-items:center;gap:12px;overflow:auto;padding:18px}}.graph-root,.graph-node{{min-width:180px;border:1px solid #23618a;border-radius:8px;padding:12px;background:#03243a}}.graph-root{{border-color:#087fff;background:#073152}}.graph-arrow{{font-size:24px;color:#62c6ff}}.envgrid{{display:grid;grid-template-columns:repeat(2,minmax(220px,1fr));gap:8px}}.envfact{{border:1px solid #173f58;border-radius:6px;padding:10px;background:#001827}}.envfact small{{display:block;color:#8daabd}}@media(max-width:700px){{.envgrid{{grid-template-columns:1fr}}}}</style>
""";
        return ConfigurationShell.Render(configuration.DisplayName, body, "applications");
    }

    private static string RenderGraph(ApplicationConfigurationContract app, ApplicationDependencyContract contract)
    {
        string nodes = string.Join("", contract.Dependencies.Select(d => $"<span class='graph-arrow'>→</span><div class='graph-node'><b>{E(d.DisplayName)}</b><small>{E(d.Interface)}{Engine(d)}</small><small>{E(d.Required ? "Required" : "Optional")} · {E(d.Criticality)}</small></div>"));
        return $"<div class='depgraph'><div class='graph-root'><b>{E(app.DisplayName)}</b><small>{E(app.ApplicationId)}</small></div>{nodes}</div>";
    }

    private static string RenderEnvironment(DetectedEnvironment e)
    {
        List<string> rows = [];
        Add(rows, "Operating System", JoinNonEmpty(e.OperatingSystem, e.OperatingSystemVersion));
        Add(rows, "CPU", JoinNonEmpty(e.CpuArchitecture, e.ProcessorCount is null ? null : $"{e.ProcessorCount} logical processors"));
        Add(rows, "Available Memory", Bytes(e.AvailableMemoryBytes));
        Add(rows, "Process Working Set", Bytes(e.ProcessWorkingSetBytes));
        Add(rows, ".NET Runtime", e.RuntimeVersion);
        Add(rows, "Network Present", e.NetworkAvailable?.ToString());
        if (e.Disks is not null) foreach (DetectedDisk d in e.Disks) Add(rows, $"Disk {d.Path}", $"Free {Bytes(d.FreeBytes)} / Total {Bytes(d.TotalBytes)}");
        if (e.Paths is not null) foreach (DetectedPath p in e.Paths) Add(rows, $"Path {p.Path}", $"Exists={p.Exists}; Writable={p.Writable?.ToString() ?? "Unknown"}");
        if (e.Endpoints is not null) foreach (DetectedEndpoint ep in e.Endpoints) Add(rows, $"Endpoint {ep.Name}", $"{ep.Endpoint} · Reachable={ep.Reachable?.ToString() ?? "Unknown"}{(string.IsNullOrWhiteSpace(ep.Detail) ? "" : " · " + ep.Detail)}");
        if (e.Hardware is not null) foreach (DetectedHardware h in e.Hardware) Add(rows, $"Hardware {h.Kind}", JoinNonEmpty(h.Name, h.Identifier, h.Detail));
        return $"<div class='envgrid'>{string.Join("", rows)}</div>";
    }

    private static void Add(List<string> rows, string name, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        rows.Add($"<div class='envfact'><b>{E(name)}</b><small>{E(value)}</small></div>");
    }

    private static int CountEnvironment(DetectedEnvironment? e)
    {
        if (e is null) return 0;
        int count = new object?[] { e.OperatingSystem, e.OperatingSystemVersion, e.CpuArchitecture, e.ProcessorCount, e.AvailableMemoryBytes, e.ProcessWorkingSetBytes, e.RuntimeVersion, e.NetworkAvailable }.Count(x => x is not null);
        return count + (e.Disks?.Count ?? 0) + (e.Paths?.Count ?? 0) + (e.Endpoints?.Count ?? 0) + (e.Hardware?.Count ?? 0);
    }

    private static string DisplayValue(RequirementEvaluation r)
        => r.Sensitive || r.Redacted ? "[REDACTED]" : E(r.SafeDisplayValue ?? (r.HasDefault ? r.DefaultValue : null));
    private static string Condition(RequirementEvaluation r) => string.IsNullOrWhiteSpace(r.ConditionalOn) ? "" : " · Conditional: " + E(r.ConditionalOn);
    private static string Engine(ApplicationDependency d) => string.IsNullOrWhiteSpace(d.Engine) ? "" : " · Engine: " + E(d.Engine);
    private static string? Join(IReadOnlyList<string>? values) => values is null ? null : string.Join(" > ", values);
    private static string JoinNonEmpty(params string?[] values) => string.Join(" · ", values.Where(v => !string.IsNullOrWhiteSpace(v)));
    private static string? Bytes(long? value) => value is null ? null : $"{value.Value / 1024d / 1024d / 1024d:N2} GiB";
    private static string E(string? value) => HtmlEncoder.Default.Encode(value ?? "—");
    private static bool FixedEquals(string expected, string actual)
    {
        byte[] a = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
        byte[] b = SHA256.HashData(Encoding.UTF8.GetBytes(actual));
        return CryptographicOperations.FixedTimeEquals(a, b);
    }
}
