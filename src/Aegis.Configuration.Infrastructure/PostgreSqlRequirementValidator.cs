using Aegis.Configuration.Application;
using Aegis.Configuration.Domain;
using Npgsql;

namespace Aegis.Configuration.Infrastructure;

public sealed class PostgreSqlRequirementValidator : IConfigurationRequirementValidator
{
    public bool CanValidate(ConfigurationRequirement requirement)
    {
        return string.Equals(requirement.Validator, "postgresql", StringComparison.OrdinalIgnoreCase);
    }

    public async ValueTask<(bool Success, string Message)> ValidateAsync(
        ConfigurationRequirement requirement,
        string value,
        CancellationToken cancellationToken = default)
    {
        try
        {
            NpgsqlConnectionStringBuilder builder = new(value);
            if (string.IsNullOrWhiteSpace(builder.Host) || string.IsNullOrWhiteSpace(builder.Database))
            {
                return (false, "PostgreSQL connection string must specify Host and Database.");
            }

            await using NpgsqlConnection connection = new(value);
            await connection.OpenAsync(cancellationToken);
            await using NpgsqlCommand command = new("SELECT 1", connection);
            object? result = await command.ExecuteScalarAsync(cancellationToken);
            return Convert.ToInt32(result) == 1
                ? (true, $"PostgreSQL reachable; database '{builder.Database}' accepted the application credential.")
                : (false, "PostgreSQL connection opened but verification query returned an unexpected result.");
        }
        catch (Exception exception) when (exception is NpgsqlException or ArgumentException or InvalidOperationException)
        {
            return (false, $"PostgreSQL verification failed: {exception.Message}");
        }
    }
}
