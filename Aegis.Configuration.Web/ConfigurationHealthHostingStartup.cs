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

            // Registration requires write access. Probe with a temporary file and remove it immediately.
            string probePath = Path.Combine(contractDirectory, $".aegis-health-{Guid.NewGuid():N}.tmp");
            try
            {
                await File.WriteAllTextAsync(probePath, "health", cancellationToken);
            }
            finally
            {
                try
                {
                    if (File.Exists(probePath)) File.Delete(probePath);
                }
                catch
                {
                    // If cleanup itself fails, the outer health result below will still reflect the
                    // original store operation. Cleanup failure must not expose paths or credentials.
                }
            }

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
