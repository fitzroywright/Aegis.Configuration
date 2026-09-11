using Aegis.Configuration.Application;
using Aegis.Configuration.Domain;
using Npgsql;

namespace Aegis.Configuration.Infrastructure;

public sealed class PostgreSqlRequirementValidator : IConfigurationRequirementValidator
{
    private static readonly TimeSpan timeout = TimeSpan.FromSeconds(10);
    public bool CanValidate(ConfigurationRequirement requirement) => string.Equals(requirement.Validator, "postgresql", StringComparison.OrdinalIgnoreCase);
    public async ValueTask<(bool Success, string Message)> ValidateAsync(ConfigurationRequirement requirement, string value, CancellationToken cancellationToken = default)
    {
        NpgsqlConnectionStringBuilder builder;
        try { builder = new(value); }
        catch (ArgumentException) { return (false, "PostgreSQL connection string is invalid."); }
        if (string.IsNullOrWhiteSpace(builder.Host) || string.IsNullOrWhiteSpace(builder.Database)) return (false, "PostgreSQL connection string must specify Host and Database.");
        using CancellationTokenSource timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        try
        {
            await using NpgsqlConnection connection = new(value);
            await connection.OpenAsync(timeoutSource.Token);
            await using NpgsqlCommand command = new("SELECT 1", connection) { CommandTimeout = 10 };
            object? result = await command.ExecuteScalarAsync(timeoutSource.Token);
            return Convert.ToInt32(result) == 1 ? (true, $"PostgreSQL reachable; database '{builder.Database}' accepted the application credential.") : (false, "PostgreSQL connection opened but verification query returned an unexpected result.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return (false, "PostgreSQL verification timed out."); }
        catch (Exception exception) when (exception is NpgsqlException or InvalidOperationException) { return (false, "PostgreSQL verification failed. Check host, database, network access and application credentials."); }
    }
}
