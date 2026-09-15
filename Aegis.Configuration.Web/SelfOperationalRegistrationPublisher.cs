using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;

[assembly: HostingStartup(typeof(Aegis.Configuration.Web.SelfRegistrationHostingStartup))]

namespace Aegis.Configuration.Web;

public sealed class SelfRegistrationHostingStartup : IHostingStartup
{
    public void Configure(IWebHostBuilder builder) => builder.ConfigureServices(services => services.AddHostedService<SelfOperationalRegistrationPublisher>());
}

internal sealed class SelfOperationalRegistrationPublisher(
    IConfiguration configuration,
    IWebHostEnvironment environment,
    ILogger<SelfOperationalRegistrationPublisher> logger) : BackgroundService
{
    private const string ApplicationId = "Aegis.Configuration";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        string? registrationKey = configuration["AegisConfiguration:RegistrationKey"];
        if (string.IsNullOrWhiteSpace(registrationKey)) return;
        string localUrl = First(configuration["Aegis:Configuration:Url"], configuration["urls"], "http://localhost:5101")!;
        Uri? endpoint = FirstHttpUrl(localUrl);
        if (endpoint is null) return;
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            await PublishAsync(endpoint, registrationKey, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        catch (Exception ex) { logger.LogWarning(ex, "Aegis.Configuration self-registration failed; Configuration remains operational."); }
    }

    private async Task PublishAsync(Uri endpoint, string registrationKey, CancellationToken ct)
    {
        string version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "unknown";
        string instanceId = Environment.MachineName;
        string contractDirectory = configuration["AegisConfiguration:ContractDirectory"] ?? Path.Combine(environment.ContentRootPath, "contracts");
        string dataDirectory = configuration["AegisConfiguration:DataDirectory"] ?? Path.Combine(environment.ContentRootPath, "data");
        string instructionDirectory = configuration["AegisConfiguration:InstructionDirectory"] ?? Path.GetFullPath(Path.Combine(environment.ContentRootPath, "..", "..", "instruction-packs"));

        object[] requirements =
        [
            Config("contract-directory", "AegisConfiguration:ContractDirectory", "Contract Directory", false, "Directory where registered configuration contracts are persisted.", "contracts"),
            Config("data-directory", "AegisConfiguration:DataDirectory", "Data Directory", false, "Configuration service operational data directory.", "data"),
            Config("instruction-directory", "AegisConfiguration:InstructionDirectory", "Instruction Directory", false, "Configuration commissioning instruction-pack directory.", "../../instruction-packs"),
            Secret("registration-key", "AegisConfiguration:RegistrationKey", "Application Registration Credential", true, "Credential accepted from publishing applications. Value is never published.", true, "AegisConfiguration:RegistrationKey"),
            Config("allowed-hosts", "AllowedHosts", "Allowed Hosts", false, "ASP.NET Core host filtering.", "*"),
            Config("secrets-mode", "CommonSecrets:Mode", "Common.Secrets Mode", false, "Common.Secrets operating mode.", "Production"),
            Config("secrets-workload", "CommonSecrets:WorkloadName", "Common.Secrets Workload", false, "OpenBao/Common.Secrets workload identity.", "Aegis.Configuration"),
            Config("openbao-address", "CommonSecrets:OpenBao:Address", "OpenBao Address", false, "OpenBao endpoint when configured."),
            Secret("secret-zero", "Secret0", "OpenBao Secret Zero", false, "Bootstrap material used by Common.Secrets to authenticate to OpenBao. Value is never published.", SecretZeroDetected(), "OpenBao bootstrap")
        ];

        object[] dependencies =
        [
            Dependency("contract-store", "Configuration Contract Store", true, "Startup", "Persists application configuration contracts.", "Registration and application inventory cannot be persisted.", "Filesystem", "Operating System Filesystem", contractDirectory, null),
            Dependency("instruction-store", "Instruction Pack Store", false, "Feature", "Provides commissioning/recovery runbook content.", "Configuration inventory remains available; instruction/runbook content is incomplete.", "Filesystem", "Operating System Filesystem", instructionDirectory, null),
            Dependency("active-directory", "Active Directory", true, "Startup", "Authenticates Configuration users through Common.Security/SuiteSecurity.", "Interactive administration sign-in is unavailable.", "LDAP/LDAPS via Common.Security", "Active Directory", null, null),
            Dependency("openbao", "OpenBao", false, "Feature", "Provides secret material through Common.Secrets when enabled.", "Secret-backed features depending on OpenBao are unavailable.", "OpenBao HTTP API via Common.Secrets", "OpenBao", OpenBaoAddress(), await ReachableAsync(OpenBaoAddress(), ct))
        ];

        object registration = new
        {
            configuration = new { applicationId = ApplicationId, displayName = "System Configuration", version, siteId = configuration["Site:Id"], instanceId, description = "Aegis configuration description and monitoring service. It does not own application configuration values.", requirements },
            dependencies = new { applicationId = ApplicationId, version, siteId = configuration["Site:Id"], instanceId, dependencies },
            environment = new { applicationId = ApplicationId, version, siteId = configuration["Site:Id"], instanceId, environment = await DetectEnvironmentAsync(new[] { contractDirectory, dataDirectory, instructionDirectory }, ct) }
        };

        using HttpClient client = new() { BaseAddress = endpoint, Timeout = TimeSpan.FromSeconds(10) };
        using HttpRequestMessage request = new(HttpMethod.Post, "api/registrations/register");
        request.Headers.TryAddWithoutValidation("X-Aegis-Registration-Key", registrationKey);
        request.Content = new StringContent(JsonSerializer.Serialize(registration), Encoding.UTF8, "application/json");
        using HttpResponseMessage response = await client.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) logger.LogWarning("Aegis.Configuration self-registration returned HTTP {StatusCode}.", (int)response.StatusCode);
    }

    private object Config(string id, string key, string name, bool required, string purpose, string? defaultValue = null)
    {
        string envKey = key.Replace(":", "__", StringComparison.Ordinal); string? env = Environment.GetEnvironmentVariable(envKey); string? configured = configuration[key]; string? value = First(env, configured, defaultValue);
        return new { id, displayName = name, kind = "Configuration", required, purpose, configurationKey = key, isConfigured = !string.IsNullOrWhiteSpace(value), safeDisplayValue = value, sensitive = false, allowedSources = new[] { "Environment Variable", $"appsettings.{environment.EnvironmentName}.json", "appsettings.json", "Code Default" }, resolutionOrder = new[] { "Environment Variable", $"appsettings.{environment.EnvironmentName}.json", "appsettings.json", "Code Default" }, effectiveSource = !string.IsNullOrWhiteSpace(env) ? $"Environment Variable ({envKey})" : !string.IsNullOrWhiteSpace(configured) ? "Configuration Provider" : defaultValue is not null ? "Code Default" : "Unresolved", hasDefault = defaultValue is not null, defaultValue, redacted = false };
    }
    private static object Secret(string id, string key, string name, bool required, string purpose, bool configured, string path) => new { id, displayName = name, kind = "Secret", required, purpose, configurationKey = key, isConfigured = configured, safeDisplayValue = (string?)null, sensitive = true, allowedSources = new[] { "OpenBao/Common.Secrets", "Environment Variable", "Secret0 bootstrap" }, resolutionOrder = new[] { "secure provider", "approved Environment Variable override", "no plaintext/display fallback" }, effectiveSource = configured ? "Secure runtime source" : "Unresolved", hasDefault = false, defaultValue = (string?)null, secretPath = path, redacted = true };
    private static object Dependency(string id, string name, bool required, string criticality, string purpose, string degraded, string @interface, string? engine, string? endpoint, bool? reachable) => new { id, displayName = name, required, criticality, purpose, degradedBehavior = degraded, @interface, engine, endpoint, reachable, evidence = "Explicitly declared from Aegis.Configuration code; no inferred engine." };

    private async Task<object> DetectEnvironmentAsync(IEnumerable<string> relevantPaths, CancellationToken ct)
    {
        Process p = Process.GetCurrentProcess(); GCMemoryInfo memory = GC.GetGCMemoryInfo(); List<object> paths = []; List<object> disks = []; HashSet<string> roots = new(StringComparer.OrdinalIgnoreCase);
        foreach (string configuredPath in relevantPaths.Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            string path = Path.GetFullPath(configuredPath); bool exists = Directory.Exists(path) || File.Exists(path); string target = Directory.Exists(path) ? path : Path.GetDirectoryName(path) ?? path; bool? writable = Directory.Exists(target) ? await IsWritableAsync(target, ct) : null; paths.Add(new { path, exists, writable });
            try { string root = Path.GetPathRoot(path) ?? ""; if (root.Length > 0 && roots.Add(root)) { DriveInfo d = new(root); if (d.IsReady) disks.Add(new { path = root, freeBytes = d.AvailableFreeSpace, totalBytes = d.TotalSize }); } } catch { }
        }
        List<object> endpoints = []; string? openBao = OpenBaoAddress(); if (!string.IsNullOrWhiteSpace(openBao)) endpoints.Add(new { name = "OpenBao", endpoint = openBao, reachable = await ReachableAsync(openBao, ct) });
        return new { operatingSystem = RuntimeInformation.OSDescription, operatingSystemVersion = Environment.OSVersion.VersionString, cpuArchitecture = RuntimeInformation.ProcessArchitecture.ToString(), processorCount = Environment.ProcessorCount, availableMemoryBytes = memory.TotalAvailableMemoryBytes > 0 ? memory.TotalAvailableMemoryBytes : null, processWorkingSetBytes = p.WorkingSet64, runtimeVersion = RuntimeInformation.FrameworkDescription, networkAvailable = NetworkInterface.GetIsNetworkAvailable(), disks, paths, endpoints, hardware = Array.Empty<object>(), detectedAtUtc = DateTimeOffset.UtcNow };
    }

    private string? OpenBaoAddress() => First(configuration["CommonSecrets:OpenBao:Address"], Environment.GetEnvironmentVariable("OPENBAO_ADDR"), Environment.GetEnvironmentVariable("VAULT_ADDR"));
    private static bool SecretZeroDetected() => new[] { "OPENBAO_SECRET_ID", "OPENBAO_TOKEN", "VAULT_TOKEN" }.Any(name => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name)));
    private static Uri? FirstHttpUrl(string urls) { foreach (string part in urls.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) if (Uri.TryCreate(part.Replace("0.0.0.0", "localhost", StringComparison.Ordinal), UriKind.Absolute, out Uri? uri) && (uri.Scheme == "http" || uri.Scheme == "https")) return new Uri(uri.GetLeftPart(UriPartial.Authority).TrimEnd('/') + "/"); return null; }
    private static async Task<bool?> ReachableAsync(string? endpoint, CancellationToken ct) { if (!Uri.TryCreate(endpoint, UriKind.Absolute, out Uri? uri) || (uri.Scheme != "http" && uri.Scheme != "https")) return null; try { using HttpClient c = new() { Timeout = TimeSpan.FromSeconds(3) }; using HttpResponseMessage _ = await c.SendAsync(new HttpRequestMessage(HttpMethod.Head, uri), ct); return true; } catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; } catch { return false; } }
    private static async Task<bool> IsWritableAsync(string path, CancellationToken ct) { string probe = Path.Combine(path, $".aegis-write-{Guid.NewGuid():N}"); try { await File.WriteAllTextAsync(probe, "", ct); File.Delete(probe); return true; } catch { try { if (File.Exists(probe)) File.Delete(probe); } catch { } return false; } }
    private static string? First(params string?[] values) => values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
}
