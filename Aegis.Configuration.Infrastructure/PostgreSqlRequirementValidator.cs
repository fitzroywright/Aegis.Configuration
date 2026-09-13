using Aegis.Configuration.Application;
using Aegis.Configuration.Domain;
using Npgsql;

namespace Aegis.Configuration.Infrastructure;

public sealed class PostgreSqlRequirementValidator : IConfigurationRequirementValidator
{
    public bool CanValidate(ConfigurationRequirement requirement) =>
        string.Equals(requirement.Validator, "postgresql", StringComparison.OrdinalIgnoreCase);

    public ValueTask<(bool Success, string Message)> ValidateAsync(
        ConfigurationRequirement requirement,
        string value,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            NpgsqlConnectionStringBuilder builder = new(value);
            bool valid = !string.IsNullOrWhiteSpace(builder.Host) && !string.IsNullOrWhiteSpace(builder.Database);
            return ValueTask.FromResult(valid
                ? (true, "PostgreSQL connection information is structurally complete. Connectivity is verified by Aegis.Diagnostics.")
                : (false, "PostgreSQL connection information must specify Host and Database."));
        }
        catch (ArgumentException)
        {
            return ValueTask.FromResult((false, "PostgreSQL connection information is not valid."));
        }
    }
}
