using Common.Diagnostics;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

[assembly: HostingStartup(typeof(Aegis.Configuration.Web.ConfigurationHealthHostingStartup))]

namespace Aegis.Configuration.Web;

public sealed class ConfigurationHealthHostingStartup : IHostingStartup
{
    public void Configure(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
            services.AddSingleton<IAegisHealthAssessmentSource, ConfigurationHealthAssessmentSource>());
    }
}

internal sealed class ConfigurationHealthAssessmentSource(
    IConfiguration configuration,
    IWebHostEnvironment environment) : IAegisHealthAssessmentSource
{
    public async Task<AegisHealthAssessment> AssessAsync(CancellationToken cancellationToken)
    {
        string contractDirectory = configuration["AegisConfiguration:ContractDirectory"]
            ?? Path.Combine(environment.ContentRootPath, "contracts");

        try
        {
            Directory.CreateDirectory(contractDirectory);

            // Reading the directory proves the registry can enumerate its backing store.
            _ = Directory.EnumerateFiles(contractDirectory).Take(1).ToArray();

            // Registration requires write/delete access. Probe with a temporary file and remove it immediately.
            string probePath = Path.Combine(contractDirectory, $".aegis-health-{Guid.NewGuid():N}.tmp");
            await File.WriteAllTextAsync(probePath, "health", cancellationToken);
            File.Delete(probePath);

            return AegisHealthAssessment.Healthy("Configuration contract store is readable and writable.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return AegisHealthAssessment.Unhealthy($"Configuration contract store is unavailable: {exception.GetType().Name}.");
        }
    }
}
