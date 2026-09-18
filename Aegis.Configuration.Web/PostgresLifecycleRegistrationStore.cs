using System.Text.Json;
using System.Text.Json.Nodes;
using Npgsql;

internal sealed class PostgresLifecycleRegistrationStore
{
    private readonly string _connectionString;
    private readonly SemaphoreSlim _initializeGate = new(1, 1);
    private bool _initialized;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    public PostgresLifecycleRegistrationStore(IConfiguration configuration)
    {
        _connectionString =
            configuration["AegisConfiguration:RegistrationConnectionString"]?.Trim()
            ?? configuration.GetConnectionString("AegisConfiguration")?.Trim()
            ?? throw new InvalidOperationException(
                "Aegis Configuration registration database is not configured. Set AegisConfiguration:RegistrationConnectionString or ConnectionStrings:AegisConfiguration.");
    }

    public async Task<IReadOnlyList<LifecycleRegistrationRecord>> LoadAllAsync(CancellationToken ct)
    {
        await EnsureInitializedAsync(ct);
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        await using var command = new NpgsqlCommand(
            """
            SELECT registration_id, application_id, instance_id, installation_id, state,
                   claim_token_hash, claim_expires_at_utc, credential_hash,
                   created_at_utc, updated_at_utc, approved_at_utc, registered_at_utc,
                   last_authenticated_at_utc, revoked_at_utc, metadata_json,
                   audit_json, conflict_installation_id
            FROM aegis_registration
            ORDER BY updated_at_utc DESC;
            """,
            connection);

        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(ct);
        var result = new List<LifecycleRegistrationRecord>();

        while (await reader.ReadAsync(ct))
            result.Add(Read(reader));

        return result;
    }

    public async Task<LifecycleRegistrationRecord?> LoadAsync(
        string registrationId,
        CancellationToken ct)
    {
        await EnsureInitializedAsync(ct);
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        await using var command = new NpgsqlCommand(
            """
            SELECT registration_id, application_id, instance_id, installation_id, state,
                   claim_token_hash, claim_expires_at_utc, credential_hash,
                   created_at_utc, updated_at_utc, approved_at_utc, registered_at_utc,
                   last_authenticated_at_utc, revoked_at_utc, metadata_json,
                   audit_json, conflict_installation_id
            FROM aegis_registration
            WHERE registration_id = @id;
            """,
            connection);

        command.Parameters.AddWithValue("id", registrationId);

        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Read(reader) : null;
    }

    public async Task SaveAsync(
        LifecycleRegistrationRecord record,
        CancellationToken ct)
    {
        await EnsureInitializedAsync(ct);
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(ct);

        await using var command = new NpgsqlCommand(
            """
            INSERT INTO aegis_registration
            (
                registration_id, application_id, instance_id, installation_id, state,
                claim_token_hash, claim_expires_at_utc, credential_hash,
                created_at_utc, updated_at_utc, approved_at_utc, registered_at_utc,
                last_authenticated_at_utc, revoked_at_utc, metadata_json,
                audit_json, conflict_installation_id
            )
            VALUES
            (
                @registration_id, @application_id, @instance_id, @installation_id, @state,
                @claim_token_hash, @claim_expires_at_utc, @credential_hash,
                @created_at_utc, @updated_at_utc, @approved_at_utc, @registered_at_utc,
                @last_authenticated_at_utc, @revoked_at_utc, CAST(@metadata_json AS jsonb),
                CAST(@audit_json AS jsonb), @conflict_installation_id
            )
            ON CONFLICT (registration_id) DO UPDATE SET
                application_id = EXCLUDED.application_id,
                instance_id = EXCLUDED.instance_id,
                installation_id = EXCLUDED.installation_id,
                state = EXCLUDED.state,
                claim_token_hash = EXCLUDED.claim_token_hash,
                claim_expires_at_utc = EXCLUDED.claim_expires_at_utc,
                credential_hash = EXCLUDED.credential_hash,
                updated_at_utc = EXCLUDED.updated_at_utc,
                approved_at_utc = EXCLUDED.approved_at_utc,
                registered_at_utc = EXCLUDED.registered_at_utc,
                last_authenticated_at_utc = EXCLUDED.last_authenticated_at_utc,
                revoked_at_utc = EXCLUDED.revoked_at_utc,
                metadata_json = EXCLUDED.metadata_json,
                audit_json = EXCLUDED.audit_json,
                conflict_installation_id = EXCLUDED.conflict_installation_id;
            """,
            connection);

        command.Parameters.AddWithValue("registration_id", record.RegistrationId);
        command.Parameters.AddWithValue("application_id", record.ApplicationId);
        command.Parameters.AddWithValue("instance_id", record.InstanceId);
        command.Parameters.AddWithValue("installation_id", record.InstallationId);
        command.Parameters.AddWithValue("state", record.State.ToString());
        command.Parameters.AddWithValue("claim_token_hash", record.ClaimTokenHash);
        command.Parameters.AddWithValue("claim_expires_at_utc", record.ClaimExpiresAtUtc);
        command.Parameters.AddWithValue("credential_hash", (object?)record.CredentialHash ?? DBNull.Value);
        command.Parameters.AddWithValue("created_at_utc", record.CreatedAtUtc);
        command.Parameters.AddWithValue("updated_at_utc", record.UpdatedAtUtc);
        command.Parameters.AddWithValue("approved_at_utc", (object?)record.ApprovedAtUtc ?? DBNull.Value);
        command.Parameters.AddWithValue("registered_at_utc", (object?)record.RegisteredAtUtc ?? DBNull.Value);
        command.Parameters.AddWithValue("last_authenticated_at_utc", (object?)record.LastAuthenticatedAtUtc ?? DBNull.Value);
        command.Parameters.AddWithValue("revoked_at_utc", (object?)record.RevokedAtUtc ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "metadata_json",
            record.Metadata?.ToJsonString() ?? "null");
        command.Parameters.AddWithValue(
            "audit_json",
            JsonSerializer.Serialize(record.Audit, JsonOptions));
        command.Parameters.AddWithValue(
            "conflict_installation_id",
            (object?)record.ConflictInstallationId ?? DBNull.Value);

        await command.ExecuteNonQueryAsync(ct);
    }

    private async Task EnsureInitializedAsync(CancellationToken ct)
    {
        if (_initialized) return;

        await _initializeGate.WaitAsync(ct);
        try
        {
            if (_initialized) return;

            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(ct);

            await using var command = new NpgsqlCommand(
                """
                CREATE TABLE IF NOT EXISTS aegis_registration
                (
                    registration_id text PRIMARY KEY,
                    application_id text NOT NULL,
                    instance_id text NOT NULL,
                    installation_id text NOT NULL,
                    state text NOT NULL,
                    claim_token_hash text NOT NULL,
                    claim_expires_at_utc timestamptz NOT NULL,
                    credential_hash text NULL,
                    created_at_utc timestamptz NOT NULL,
                    updated_at_utc timestamptz NOT NULL,
                    approved_at_utc timestamptz NULL,
                    registered_at_utc timestamptz NULL,
                    last_authenticated_at_utc timestamptz NULL,
                    revoked_at_utc timestamptz NULL,
                    metadata_json jsonb NULL,
                    audit_json jsonb NOT NULL DEFAULT '[]'::jsonb,
                    conflict_installation_id text NULL
                );

                CREATE UNIQUE INDEX IF NOT EXISTS ux_aegis_registration_active_instance
                    ON aegis_registration (lower(application_id), lower(instance_id))
                    WHERE state <> 'Purged';

                CREATE INDEX IF NOT EXISTS ix_aegis_registration_state
                    ON aegis_registration (state);

                CREATE INDEX IF NOT EXISTS ix_aegis_registration_last_authenticated
                    ON aegis_registration (last_authenticated_at_utc);
                """,
                connection);

            await command.ExecuteNonQueryAsync(ct);
            _initialized = true;
        }
        finally
        {
            _initializeGate.Release();
        }
    }

    private static LifecycleRegistrationRecord Read(NpgsqlDataReader reader)
    {
        string stateText = reader.GetString(4);
        if (!Enum.TryParse(stateText, ignoreCase: true, out LifecycleRegistrationState state))
            throw new InvalidOperationException($"Unknown registration state '{stateText}'.");

        JsonObject? metadata = null;
        if (!reader.IsDBNull(14))
        {
            string json = reader.GetString(14);
            metadata = JsonNode.Parse(json) as JsonObject;
        }

        string auditJson = reader.GetString(15);
        List<LifecycleAuditEvent> audit =
            JsonSerializer.Deserialize<List<LifecycleAuditEvent>>(auditJson, JsonOptions)
            ?? [];

        return new LifecycleRegistrationRecord(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            state,
            reader.GetString(5),
            reader.GetFieldValue<DateTimeOffset>(6),
            reader.IsDBNull(7) ? null : reader.GetString(7),
            reader.GetFieldValue<DateTimeOffset>(8),
            reader.GetFieldValue<DateTimeOffset>(9),
            reader.IsDBNull(10) ? null : reader.GetFieldValue<DateTimeOffset>(10),
            reader.IsDBNull(11) ? null : reader.GetFieldValue<DateTimeOffset>(11),
            reader.IsDBNull(12) ? null : reader.GetFieldValue<DateTimeOffset>(12),
            reader.IsDBNull(13) ? null : reader.GetFieldValue<DateTimeOffset>(13),
            metadata,
            audit)
        {
            ConflictInstallationId = reader.IsDBNull(16) ? null : reader.GetString(16)
        };
    }
}
