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
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        return app =>
        {
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
            next(app);
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
        if (!FixedEquals(configuredKey, supplied))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        ApplicationOperationalRegistration? registration;
        try
        {
            registration = await JsonSerializer.DeserializeAsync<ApplicationOperationalRegistration>(context.Request.Body, Json, context.RequestAborted);
        }
        catch (JsonException exception)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsync(exception.Message);
            return;
        }
        if (registration is null)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        ContractValidator validator = context.RequestServices.GetRequiredService<ContractValidator>();
        ContractValidationResult validation = validator.Validate(registration.Configuration);
        if (!validation.IsValid)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await JsonAsync(context, validation);
            return;
        }

        // Defense in depth: sensitive values are discarded before anything is written to disk.
        ApplicationConfigurationContract sanitized = registration.Configuration with
        {
            Requirements = registration.Configuration.Requirements.Select(requirement => requirement.Sensitive
                ? requirement with { SafeDisplayValue = null, DefaultValue = null, Redacted = true }
                : requirement).ToArray()
        };
        registration = registration with { Configuration = sanitized };

        IConfigurationContractRegistrationStore configurationStore = context.RequestServices.GetRequiredService<IConfigurationContractRegistrationStore>();
        DirectoryOperationalContractStore operationalStore = context.RequestServices.GetRequiredService<DirectoryOperationalContractStore>();
        await configurationStore.RegisterAsync(sanitized, context.RequestAborted);
        await operationalStore.RegisterAsync(registration, context.RequestAborted);
        await JsonAsync(context, new
        {
            registered = true,
            applicationId = sanitized.ApplicationId,
            contracts = new[] { "configuration", "dependencies", "environment" }
        });
    }

    private static async Task RenderAsync(HttpContext context, string applicationId)
    {
        IConfigurationContractStore configurationStore = context.RequestServices.GetRequiredService<IConfigurationContractStore>();
        ConfigurationEvaluator evaluator = context.RequestServices.GetRequiredService<ConfigurationEvaluator>();
        DirectoryOperationalContractStore operationalStore = context.RequestServices.GetRequiredService<DirectoryOperationalContractStore>();

        ApplicationConfigurationContract? contract = (await configurationStore.LoadAllAsync(context.RequestAborted))
            .FirstOrDefault(candidate => string.Equals(candidate.ApplicationId, applicationId, StringComparison.OrdinalIgnoreCase));
        if (contract is null)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        ConfigurationEvaluation evaluation = await evaluator.EvaluateAsync(contract, context.RequestAborted);
        ApplicationDependencyContract? dependencies = await operationalStore.LoadDependenciesAsync(applicationId, context.RequestAborted);
        ApplicationEnvironmentContract? detectedEnvironment = await operationalStore.LoadEnvironmentAsync(applicationId, context.RequestAborted);

        string configurationHtml = string.Join("", evaluation.Requirements.Select(requirement =>
            $"<div class='req'><span><b>{E(requirement.DisplayName)}</b><small>{E(requirement.Purpose)}{Condition(requirement)}</small><small>Source: {E(requirement.EffectiveSource)} · Priority: {E(Join(requirement.ResolutionOrder))}</small></span><code>{DisplayValue(requirement)}</code><i class='{(requirement.State == ConfigurationState.Configured ? "ok" : "bad")}'>{(requirement.State == ConfigurationState.Configured ? "✓" : "!")}</i></div>"));

        string dependencyHtml = dependencies is null || dependencies.Dependencies.Count == 0
            ? "<div class='empty'>No dependency contract published.</div>"
            : string.Join("", dependencies.Dependencies.Select(dependency =>
                $"<div class='req'><span><b>{E(dependency.DisplayName)}</b><small>{E(dependency.Purpose)}</small><small>{(dependency.Required ? "Required" : "Optional")} · {E(dependency.Criticality)} · Interface: {E(dependency.Interface)}{Engine(dependency)}</small><small>Degraded: {E(dependency.DegradedBehavior)}</small></span><code>{E(Reachability(dependency.Reachable))}</code><i class='{(dependency.Reachable is false && dependency.Required ? "bad" : "ok")}'>{(dependency.Reachable is false && dependency.Required ? "!" : "✓")}</i></div>"));

        string graphHtml = dependencies is null || dependencies.Dependencies.Count == 0
            ? "<div class='empty'>No dependency graph available.</div>"
            : $"<div class='depgraph'><div class='graph-root'><b>{E(contract.DisplayName)}</b><small>{E(contract.ApplicationId)}</small></div>{string.Join("", dependencies.Dependencies.Select(dependency => $"<span class='graph-arrow'>→</span><div class='graph-node'><b>{E(dependency.DisplayName)}</b><small>{E(dependency.Interface)}{Engine(dependency)}</small><small>{(dependency.Required ? "Required" : "Optional")} · {E(dependency.Criticality)}</small></div>"))}</div>";

        string environmentHtml = detectedEnvironment is null
            ? "<div class='empty'>No detected environment published.</div>"
            : EnvironmentHtml(detectedEnvironment.Environment);

        string body = $"""
<section class='page-head'><div><h1>{E(contract.DisplayName)}</h1><p>Configuration, dependencies and detected environment</p></div></section>
<section class='metrics'>
  <div class='metric'><span>≡</span><b>{evaluation.Requirements.Count}</b><small>Configuration Items</small></div>
  <div class='metric green'><span>⌁</span><b>{dependencies?.Dependencies.Count ?? 0}</b><small>Dependencies</small></div>
  <div class='metric'><span>◈</span><b>{EnvironmentFactCount(detectedEnvironment?.Environment)}</b><small>Detected Facts</small></div>
  <div class='metric amber'><span>!</span><b>{evaluation.Requirements.Count(requirement => requirement.State == ConfigurationState.Missing)}</b><small>Missing Required</small></div>
</section>
<section class='panel contract'><h2>Configuration</h2><p class='muted'>Applications report their configuration contract and effective sources. Configuration does not own these values. Secrets are always redacted.</p>{configurationHtml}</section>
<section class='panel contract'><h2>Dependencies</h2><p class='muted'>Dependencies are explicit declarations from application code. Engines are not inferred from connection strings.</p>{dependencyHtml}</section>
<section class='panel contract'><h2>Dependency Graph</h2>{graphHtml}</section>
<section class='panel contract'><h2>Detected Environment</h2><p class='muted'>Direct runtime observations only. No manual environment requirements, estimates or guesses.</p>{environmentHtml}</section>
<div class='actions'><a href='/applications/{Uri.EscapeDataString(applicationId)}'>Back to Application</a><a href='/api/dependencies'>Dependency API</a><a href='/api/environments'>Environment API</a></div>
<style>
.contract{{padding:16px;margin:14px 0}}.depgraph{{display:flex;align-items:center;gap:12px;overflow:auto;padding:18px}}.graph-root,.graph-node{{min-width:180px;border:1px solid #23618a;border-radius:8px;padding:12px;background:#03243a}}.graph-root{{border-color:#087fff;background:#073152}}.graph-node small,.graph-root small{{display:block;color:#8daabd}}.graph-arrow{{font-size:24px;color:#62c6ff}}.envgrid{{display:grid;grid-template-columns:repeat(2,minmax(220px,1fr));gap:8px}}.envfact{{border:1px solid #173f58;border-radius:6px;padding:10px;background:#001827}}.envfact small{{display:block;color:#8daabd}}@media(max-width:700px){{.envgrid{{grid-template-columns:1fr}}}}
</style>
""";

        context.Response.ContentType = "text/html; charset=utf-8";
        await context.Response.WriteAsync(ConfigurationShell.Render(contract.DisplayName, body, "applications"));
    }

    private static string EnvironmentHtml(DetectedEnvironment environment)
    {
        List<string> facts = [];
        AddFact(facts, "Operating System", Join(environment.OperatingSystem, environment.OperatingSystemVersion));
        AddFact(facts, "CPU", Join(environment.CpuArchitecture, environment.ProcessorCount is null ? null : $"{environment.ProcessorCount} logical processors"));
        AddFact(facts, "Available Memory", Bytes(environment.AvailableMemoryBytes));
        AddFact(facts, "Process Working Set", Bytes(environment.ProcessWorkingSetBytes));
        AddFact(facts, ".NET Runtime", environment.RuntimeVersion);
        AddFact(facts, "Network Present", environment.NetworkAvailable?.ToString());
        if (environment.Disks is not null)
            foreach (DetectedDisk disk in environment.Disks)
                AddFact(facts, $"Disk {disk.Path}", $"Free {Bytes(disk.FreeBytes)} / Total {Bytes(disk.TotalBytes)}");
        if (environment.Paths is not null)
            foreach (DetectedPath path in environment.Paths)
                AddFact(facts, $"Path {path.Path}", $"Exists={path.Exists}; Writable={path.Writable?.ToString() ?? "Unknown"}");
        if (environment.Endpoints is not null)
            foreach (DetectedEndpoint endpoint in environment.Endpoints)
                AddFact(facts, $"Endpoint {endpoint.Name}", $"{endpoint.Endpoint} · Reachable={endpoint.Reachable?.ToString() ?? "Unknown"}{(string.IsNullOrWhiteSpace(endpoint.Detail) ? "" : " · " + endpoint.Detail)}");
        if (environment.Hardware is not null)
            foreach (DetectedHardware hardware in environment.Hardware)
                AddFact(facts, $"Hardware {hardware.Kind}", Join(hardware.Name, hardware.Identifier, hardware.Detail));
        return $"<div class='envgrid'>{string.Join("", facts)}</div>";
    }

    private static void AddFact(List<string> facts, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)) facts.Add($"<div class='envfact'><b>{E(name)}</b><small>{E(value)}</small></div>");
    }

    private static int EnvironmentFactCount(DetectedEnvironment? environment)
    {
        if (environment is null) return 0;
        int scalarCount = new object?[]
        {
            environment.OperatingSystem,
            environment.OperatingSystemVersion,
            environment.CpuArchitecture,
            environment.ProcessorCount,
            environment.AvailableMemoryBytes,
            environment.ProcessWorkingSetBytes,
            environment.RuntimeVersion,
            environment.NetworkAvailable
        }.Count(value => value is not null);
        return scalarCount + (environment.Disks?.Count ?? 0) + (environment.Paths?.Count ?? 0) + (environment.Endpoints?.Count ?? 0) + (environment.Hardware?.Count ?? 0);
    }

    private static string DisplayValue(RequirementEvaluation requirement)
        => requirement.Sensitive || requirement.Redacted ? "[REDACTED]" : E(requirement.SafeDisplayValue ?? (requirement.HasDefault ? requirement.DefaultValue : null));
    private static string Condition(RequirementEvaluation requirement)
        => string.IsNullOrWhiteSpace(requirement.ConditionalOn) ? "" : " · Conditional: " + E(requirement.ConditionalOn);
    private static string Engine(ApplicationDependency dependency)
        => string.IsNullOrWhiteSpace(dependency.Engine) ? "" : " · Engine: " + E(dependency.Engine);
    private static string Reachability(bool? reachable) => reachable is true ? "Reachable" : reachable is false ? "Unreachable" : "Not tested";
    private static string? Join(IReadOnlyList<string>? values) => values is null ? null : string.Join(" > ", values);
    private static string Join(params string?[] values) => string.Join(" · ", values.Where(value => !string.IsNullOrWhiteSpace(value)));
    private static string? Bytes(long? value) => value is null ? null : $"{value.Value / 1024d / 1024d / 1024d:N2} GiB";
    private static string E(string? value) => HtmlEncoder.Default.Encode(value ?? "—");

    private static bool FixedEquals(string expected, string actual)
    {
        byte[] expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
        byte[] actualHash = SHA256.HashData(Encoding.UTF8.GetBytes(actual));
        return CryptographicOperations.FixedTimeEquals(expectedHash, actualHash);
    }

    private static async Task JsonAsync<T>(HttpContext context, T value)
    {
        context.Response.ContentType = "application/json; charset=utf-8";
        await JsonSerializer.SerializeAsync(context.Response.Body, value, Json, context.RequestAborted);
    }
}
