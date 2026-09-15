using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Aegis.Configuration.Application;
using Aegis.Configuration.Domain;
using Aegis.Configuration.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

[assembly: HostingStartup(typeof(OperationalContractsHostingStartup))]

internal sealed class OperationalContractsHostingStartup : IHostingStartup
{
    public void Configure(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            services.AddSingleton(sp =>
            {
                IConfiguration configuration = sp.GetRequiredService<IConfiguration>();
                IWebHostEnvironment environment = sp.GetRequiredService<IWebHostEnvironment>();
                string directory = configuration["AegisConfiguration:ContractDirectory"] ?? Path.Combine(environment.ContentRootPath, "contracts");
                return new DirectoryOperationalContractStore(directory);
            });
            services.AddSingleton<IStartupFilter, OperationalContractsStartupFilter>();
        });
    }
}

internal sealed class OperationalContractsStartupFilter : IStartupFilter
{
    private static readonly JsonSerializerOptions json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        return app =>
        {
            next(app);
            app.Use(async (context, continuation) =>
            {
                string path = context.Request.Path.Value ?? string.Empty;
                if (context.Request.Method == "POST" && path.Equals("/api/registrations/register", StringComparison.OrdinalIgnoreCase))
                {
                    await RegisterAsync(context);
                    return;
                }
                if (context.Request.Method == "GET" && path.Equals("/api/dependencies", StringComparison.OrdinalIgnoreCase))
                {
                    DirectoryOperationalContractStore store = context.RequestServices.GetRequiredService<DirectoryOperationalContractStore>();
                    await JsonAsync(context, await store.LoadAllDependenciesAsync(context.RequestAborted));
                    return;
                }
                if (context.Request.Method == "GET" && path.Equals("/api/environments", StringComparison.OrdinalIgnoreCase))
                {
                    DirectoryOperationalContractStore store = context.RequestServices.GetRequiredService<DirectoryOperationalContractStore>();
                    await JsonAsync(context, await store.LoadAllEnvironmentsAsync(context.RequestAborted));
                    return;
                }
                const string prefix = "/applications/";
                const string suffix = "/operational";
                if (context.Request.Method == "GET" && path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    string applicationId = Uri.UnescapeDataString(path[prefix.Length..^suffix.Length].Trim('/'));
                    await RenderAsync(context, applicationId);
                    return;
                }
                await continuation();
            });
        };
    }

    private static async Task RegisterAsync(HttpContext context)
    {
        IConfiguration configuration = context.RequestServices.GetRequiredService<IConfiguration>();
        string? configuredKey = configuration["AegisConfiguration:RegistrationKey"];
        if (string.IsNullOrWhiteSpace(configuredKey))
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await context.Response.WriteAsync("Application registration is disabled because no registration credential is configured.");
            return;
        }
        string supplied = context.Request.Headers["X-Aegis-Registration-Key"].ToString();
        if (!FixedEquals(configuredKey, supplied)) { context.Response.StatusCode = StatusCodes.Status401Unauthorized; return; }

        ApplicationOperationalRegistration? registration;
        try { registration = await JsonSerializer.DeserializeAsync<ApplicationOperationalRegistration>(context.Request.Body, json, context.RequestAborted); }
        catch (JsonException ex) { context.Response.StatusCode = StatusCodes.Status400BadRequest; await context.Response.WriteAsync(ex.Message); return; }
        if (registration is null) { context.Response.StatusCode = StatusCodes.Status400BadRequest; return; }

        ContractValidator validator = context.RequestServices.GetRequiredService<ContractValidator>();
        ContractValidationResult validation = validator.Validate(registration.Configuration);
        if (!validation.IsValid) { context.Response.StatusCode = StatusCodes.Status400BadRequest; await JsonAsync(context, validation); return; }

        // Defense in depth: a sensitive requirement must never retain a display/default value at rest.
        ApplicationConfigurationContract sanitizedConfiguration = registration.Configuration with
        {
            Requirements = registration.Configuration.Requirements.Select(r => r.Sensitive
                ? r with { SafeDisplayValue = null, DefaultValue = null, Redacted = true }
                : r).ToArray()
        };
        registration = registration with { Configuration = sanitizedConfiguration };

        IConfigurationContractRegistrationStore configurationStore = context.RequestServices.GetRequiredService<IConfigurationContractRegistrationStore>();
        DirectoryOperationalContractStore operationalStore = context.RequestServices.GetRequiredService<DirectoryOperationalContractStore>();
        await configurationStore.RegisterAsync(registration.Configuration, context.RequestAborted);
        await operationalStore.RegisterAsync(registration, context.RequestAborted);
        await JsonAsync(context, new { registered = true, applicationId = registration.Configuration.ApplicationId, contracts = new[] { "configuration", "dependencies", "environment" } });
    }

    private static async Task RenderAsync(HttpContext context, string applicationId)
    {
        IConfigurationContractStore configurations = context.RequestServices.GetRequiredService<IConfigurationContractStore>();
        ConfigurationEvaluator evaluator = context.RequestServices.GetRequiredService<ConfigurationEvaluator>();
        DirectoryOperationalContractStore operational = context.RequestServices.GetRequiredService<DirectoryOperationalContractStore>();
        ApplicationConfigurationContract? contract = (await configurations.LoadAllAsync(context.RequestAborted)).FirstOrDefault(x => string.Equals(x.ApplicationId, applicationId, StringComparison.OrdinalIgnoreCase));
        if (contract is null) { context.Response.StatusCode = StatusCodes.Status404NotFound; return; }
        ConfigurationEvaluation evaluation = await evaluator.EvaluateAsync(contract, context.RequestAborted);
        ApplicationDependencyContract? deps = await operational.LoadDependenciesAsync(applicationId, context.RequestAborted);
        ApplicationEnvironmentContract? env = await operational.LoadEnvironmentAsync(applicationId, context.RequestAborted);

        string cfg = string.Join("", evaluation.Requirements.Select(r => $"<div class='req'><span><b>{E(r.DisplayName)}</b><small>{E(r.Purpose)}{(string.IsNullOrWhiteSpace(r.ConditionalOn) ? "" : " · Conditional: " + E(r.ConditionalOn))}</small><small>Source: {E(r.EffectiveSource)} · Priority: {E(r.ResolutionOrder is null ? null : string.Join(" > ", r.ResolutionOrder))}</small></span><code>{(r.Sensitive || r.Redacted ? "[REDACTED]" : E(r.SafeDisplayValue ?? (r.HasDefault ? r.DefaultValue : null)))}</code><i class='{(r.State == ConfigurationState.Configured ? "ok" : "bad")}'>{(r.State == ConfigurationState.Configured ? "✓" : "!")}</i></div>"));
        string dep = deps is null || deps.Dependencies.Count == 0 ? "<div class='empty'>No dependency contract published.</div>" : string.Join("", deps.Dependencies.Select(d => $"<div class='req'><span><b>{E(d.DisplayName)}</b><small>{E(d.Purpose)}</small><small>{(d.Required ? "Required" : "Optional")} · {E(d.Criticality)} · Interface: {E(d.Interface)}{(string.IsNullOrWhiteSpace(d.Engine) ? "" : " · Engine: " + E(d.Engine))}</small><small>Degraded behavior: {E(d.DegradedBehavior)}</small></span><code>{E(d.Reachable is true ? "Reachable" : d.Reachable is false ? "Unreachable" : "Not tested")}</code><i class='{(d.Reachable is false && d.Required ? "bad" : "ok")}'>{(d.Reachable is false && d.Required ? "!" : "✓")}</i></div>"));
        string graph = deps is null || deps.Dependencies.Count == 0 ? "<div class='empty'>No dependency graph available.</div>" : $"<div class='depgraph'><div class='graph-root'><b>{E(contract.DisplayName)}</b></div>{string.Join("", deps.Dependencies.Select(d => $"<span>→</span><div class='graph-node'><b>{E(d.DisplayName)}</b><small>{E(d.Interface)}{(string.IsNullOrWhiteSpace(d.Engine) ? "" : " · " + E(d.Engine))}</small></div>"))}</div>";
        string detected = env is null ? "<div class='empty'>No detected environment published.</div>" : EnvironmentHtml(env.Environment);
        string body = $"<section class='page-head'><div><h1>{E(contract.DisplayName)}</h1><p>Configuration, dependencies and detected environment</p></div></section><section class='panel contract'><h2>Configuration</h2><p class='muted'>Values remain owned by their real sources. Secrets are always redacted.</p>{cfg}</section><section class='panel contract'><h2>Dependencies</h2>{dep}</section><section class='panel contract'><h2>Dependency Graph</h2>{graph}</section><section class='panel contract'><h2>Detected Environment</h2><p class='muted'>Runtime-detected facts only; no declared requirements, estimates or guesses.</p>{detected}</section><div class='actions'><a href='/applications/{Uri.EscapeDataString(applicationId)}'>Back to Application</a><a href='/api/dependencies'>Dependency API</a><a href='/api/environments'>Environment API</a></div><style>.contract{{padding:16px;margin:14px 0}}.depgraph{{display:flex;align-items:center;gap:12px;overflow:auto;padding:18px}}.graph-root,.graph-node{{min-width:180px;border:1px solid #23618a;border-radius:8px;padding:12px;background:#03243a}}.graph-root{{border-color:#087fff;background:#073152}}.graph-node small{{display:block;color:#8daabd}}.depgraph>span{{font-size:24px;color:#62c6ff}}.envgrid{{display:grid;grid-template-columns:repeat(2,minmax(220px,1fr));gap:8px}}.envfact{{border:1px solid #173f58;border-radius:6px;padding:10px;background:#001827}}.envfact small{{display:block;color:#8daabd}}@media(max-width:700px){{.envgrid{{grid-template-columns:1fr}}}}</style>";
        context.Response.ContentType = "text/html; charset=utf-8";
        await context.Response.WriteAsync(ConfigurationShell.Render(contract.DisplayName, body, "applications"));
    }

    private static string EnvironmentHtml(DetectedEnvironment e)
    {
        List<string> rows = [];
        Add(rows, "Operating System", Join(e.OperatingSystem, e.OperatingSystemVersion));
        Add(rows, "CPU", Join(e.CpuArchitecture, e.ProcessorCount is null ? null : $"{e.ProcessorCount} logical processors"));
        Add(rows, "Available Memory", Bytes(e.AvailableMemoryBytes));
        Add(rows, "Process Working Set", Bytes(e.ProcessWorkingSetBytes));
        Add(rows, ".NET Runtime", e.RuntimeVersion);
        Add(rows, "Network Present", e.NetworkAvailable?.ToString());
        if (e.Disks is not null) foreach (DetectedDisk d in e.Disks) Add(rows, $"Disk {d.Path}", $"Free {Bytes(d.FreeBytes)} / Total {Bytes(d.TotalBytes)}");
        if (e.Paths is not null) foreach (DetectedPath p in e.Paths) Add(rows, $"Path {p.Path}", $"Exists={p.Exists}; Writable={p.Writable?.ToString() ?? "Unknown"}");
        if (e.Endpoints is not null) foreach (DetectedEndpoint ep in e.Endpoints) Add(rows, $"Endpoint {ep.Name}", $"{ep.Endpoint} · Reachable={ep.Reachable?.ToString() ?? "Unknown"}{(string.IsNullOrWhiteSpace(ep.Detail) ? "" : " · " + ep.Detail)}");
        if (e.Hardware is not null) foreach (DetectedHardware h in e.Hardware) Add(rows, $"Hardware {h.Kind}", Join(h.Name, h.Identifier, h.Detail));
        return $"<div class='envgrid'>{string.Join("", rows)}</div>";
    }

    private static void Add(List<string> rows, string name, string? value) { if (!string.IsNullOrWhiteSpace(value)) rows.Add($"<div class='envfact'><b>{E(name)}</b><small>{E(value)}</small></div>"); }
    private static string Join(params string?[] values) => string.Join(" · ", values.Where(v => !string.IsNullOrWhiteSpace(v)));
    private static string? Bytes(long? value) => value is null ? null : $"{value.Value / 1024d / 1024d / 1024d:N2} GiB";
    private static string E(string? value) => HtmlEncoder.Default.Encode(value ?? "—");
    private static bool FixedEquals(string expected, string actual)
    {
        byte[] a = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
        byte[] b = SHA256.HashData(Encoding.UTF8.GetBytes(actual));
        return CryptographicOperations.FixedTimeEquals(a, b);
    }
    private static async Task JsonAsync<T>(HttpContext context, T value)
    {
        context.Response.ContentType = "application/json; charset=utf-8";
        await JsonSerializer.SerializeAsync(context.Response.Body, value, json, context.RequestAborted);
    }
}
