using System.Text.Json;
using System.Text.Json.Nodes;
using Npgsql;

internal interface ILifecycleRegistrationStore
{
    Task<IReadOnlyList<LifecycleRegistrationRecord>> LoadAllAsync(CancellationToken ct);
    Task<LifecycleRegistrationRecord?> LoadAsync(string registrationId, CancellationToken ct);
    Task<LifecycleRegistrationRecord?> LoadActiveAsync(string applicationId, string instanceId, CancellationToken ct);
    Task SaveAsync(LifecycleRegistrationRecord record, CancellationToken ct);
}

internal sealed class PostgresLifecycleRegistrationStore : ILifecycleRegistrationStore
{
    private readonly string _connectionString;
    private readonly RegistrationLifecycleTelemetryPublisher _telemetry;
    private readonly SemaphoreSlim _initializeGate = new(1, 1);
    private bool _initialized;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    public PostgresLifecycleRegistrationStore(
        IConfiguration configuration,
        RegistrationLifecycleTelemetryPublisher telemetry)
    {
        _telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
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
                   conflict_installation_id, active_runtime_session_id,
                   runtime_lease_expires_at_utc, conflict_runtime_session_id, credential_scopes_json
            FROM aegis_registration
            ORDER BY updated_at_utc DESC;
            """,
            connection);

        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(ct);
        var result = new List<LifecycleRegistrationRecord>();

        while (await reader.ReadAsync(ct))
            result.Add(ReadBase(reader));

        await reader.CloseAsync();

        IReadOnlyDictionary<string, List<LifecycleAuditEvent>> audit =
            await LoadAuditMapAsync(connection, ct);

        return result
            .Select(record => record with
            {
                Audit = audit.TryGetValue(record.RegistrationId, out List<LifecycleAuditEvent>? events)
                    ? events
                    : []
            })
            .ToList();
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
                   conflict_installation_id, active_runtime_session_id,
                   runtime_lease_expires_at_utc, conflict_runtime_session_id, credential_scopes_json
            FROM aegis_registration
            WHERE registration_id = @id;
            """,
            connection);

        command.Parameters.AddWithValue("id", registrationId);

        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return null;

        LifecycleRegistrationRecord record = ReadBase(reader);
        await reader.CloseAsync();

        List<LifecycleAuditEvent> audit =
            await LoadAuditAsync(connection, record.RegistrationId, ct);

        return record with { Audit = audit };
    }

    public async Task<LifecycleRegistrationRecord?> LoadActiveAsync(
        string applicationId,
        string instanceId,
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
                   conflict_installation_id, active_runtime_session_id,
                   runtime_lease_expires_at_utc, conflict_runtime_session_id, credential_scopes_json
            FROM aegis_registration
            WHERE lower(application_id) = lower(@application_id)
              AND lower(instance_id) = lower(@instance_id)
              AND state <> 'Purged'
            ORDER BY created_at_utc DESC
            LIMIT 1;
            """,
            connection);

        command.Parameters.AddWithValue("application_id", applicationId);
        command.Parameters.AddWithValue("instance_id", instanceId);

        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return null;

        LifecycleRegistrationRecord record = ReadBase(reader);
        await reader.CloseAsync();

        List<LifecycleAuditEvent> audit =
            await LoadAuditAsync(connection, record.RegistrationId, ct);

        return record with { Audit = audit };
    }

    public async Task SaveAsync(
        LifecycleRegistrationRecord record,
        CancellationToken ct)
    {
        await EnsureInitializedAsync(ct);
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(ct);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(ct);

        await using var command = new NpgsqlCommand(
            """
            INSERT INTO aegis_registration
            (
                registration_id, application_id, instance_id, installation_id, state,
                claim_token_hash, claim_expires_at_utc, credential_hash,
                created_at_utc, updated_at_utc, approved_at_utc, registered_at_utc,
                last_authenticated_at_utc, revoked_at_utc, metadata_json,
                audit_json, conflict_installation_id, active_runtime_session_id,
                runtime_lease_expires_at_utc, conflict_runtime_session_id, credential_scopes_json
            )
            VALUES
            (
                @registration_id, @application_id, @instance_id, @installation_id, @state,
                @claim_token_hash, @claim_expires_at_utc, @credential_hash,
                @created_at_utc, @updated_at_utc, @approved_at_utc, @registered_at_utc,
                @last_authenticated_at_utc, @revoked_at_utc, CAST(@metadata_json AS jsonb),
                CAST(@audit_json AS jsonb), @conflict_installation_id, @active_runtime_session_id,
                @runtime_lease_expires_at_utc, @conflict_runtime_session_id, CAST(@credential_scopes_json AS jsonb)
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
                conflict_installation_id = EXCLUDED.conflict_installation_id,
                active_runtime_session_id = EXCLUDED.active_runtime_session_id,
                runtime_lease_expires_at_utc = EXCLUDED.runtime_lease_expires_at_utc,
                conflict_runtime_session_id = EXCLUDED.conflict_runtime_session_id,
                credential_scopes_json = EXCLUDED.credential_scopes_json;
            """,
            connection,
            transaction);

        command.Parameters.AddWithValue("credential_scopes_json",JsonSerializer.Serialize(record.CredentialScopes));
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
            "[]");
        command.Parameters.AddWithValue(
            "conflict_installation_id",
            (object?)record.ConflictInstallationId ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "active_runtime_session_id",
            (object?)record.ActiveRuntimeSessionId ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "runtime_lease_expires_at_utc",
            (object?)record.RuntimeLeaseExpiresAtUtc ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "conflict_runtime_session_id",
            (object?)record.ConflictRuntimeSessionId ?? DBNull.Value);

        await command.ExecuteNonQueryAsync(ct);

        bool latestAuditInserted = false;
        if (record.Audit.Count > 0)
        {
            int eventIndex = record.Audit.Count - 1;
            LifecycleAuditEvent audit = record.Audit[eventIndex];
            await using var auditCommand = new NpgsqlCommand(
                """
                INSERT INTO aegis_registration_audit
                (
                    registration_id, event_index, at_utc, action, actor,
                    source_ip, user_agent, reason, correlation_id,
                    previous_state, resulting_state
                )
                VALUES
                (
                    @registration_id, @event_index, @at_utc, @action, @actor,
                    @source_ip, @user_agent, @reason, @correlation_id,
                    @previous_state, @resulting_state
                )
                ON CONFLICT (registration_id, event_index) DO NOTHING;
                """,
                connection,
                transaction);

            auditCommand.Parameters.AddWithValue("registration_id", record.RegistrationId);
            auditCommand.Parameters.AddWithValue("event_index", eventIndex);
            auditCommand.Parameters.AddWithValue("at_utc", audit.AtUtc);
            auditCommand.Parameters.AddWithValue("action", audit.Action);
            auditCommand.Parameters.AddWithValue("actor", audit.Actor);
            auditCommand.Parameters.AddWithValue("source_ip", (object?)audit.SourceIp ?? DBNull.Value);
            auditCommand.Parameters.AddWithValue("user_agent", (object?)audit.UserAgent ?? DBNull.Value);
            auditCommand.Parameters.AddWithValue("reason", (object?)audit.Reason ?? DBNull.Value);
            auditCommand.Parameters.AddWithValue("correlation_id", (object?)audit.CorrelationId ?? DBNull.Value);
            auditCommand.Parameters.AddWithValue("previous_state", (object?)audit.PreviousState?.ToString() ?? DBNull.Value);
            auditCommand.Parameters.AddWithValue("resulting_state", audit.ResultingState.ToString());
            latestAuditInserted = await auditCommand.ExecuteNonQueryAsync(ct) > 0;
        }

        await transaction.CommitAsync(ct);

        if (latestAuditInserted)
            await _telemetry.PublishAsync(record, record.Audit[^1], CancellationToken.None);
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
            await using var migration=await connection.BeginTransactionAsync(ct);

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
                    conflict_installation_id text NULL,
                    active_runtime_session_id text NULL,
                    runtime_lease_expires_at_utc timestamptz NULL,
                    conflict_runtime_session_id text NULL
                );

                ALTER TABLE aegis_registration
                    ADD COLUMN IF NOT EXISTS audit_json jsonb NOT NULL DEFAULT '[]'::jsonb;

                ALTER TABLE aegis_registration
                    ADD COLUMN IF NOT EXISTS conflict_installation_id text NULL;

                ALTER TABLE aegis_registration
                    ADD COLUMN IF NOT EXISTS active_runtime_session_id text NULL;

                ALTER TABLE aegis_registration
                    ADD COLUMN IF NOT EXISTS runtime_lease_expires_at_utc timestamptz NULL;

                ALTER TABLE aegis_registration
                    ADD COLUMN IF NOT EXISTS conflict_runtime_session_id text NULL;

                CREATE UNIQUE INDEX IF NOT EXISTS ux_aegis_registration_active_instance
                    ON aegis_registration (lower(application_id), lower(instance_id))
                    WHERE state <> 'Purged';

                ALTER TABLE aegis_registration ADD COLUMN IF NOT EXISTS credential_scopes_json jsonb NOT NULL
                    DEFAULT '["registration.authenticate","operations.telemetry.publish","configuration.contract.publish"]'::jsonb;

                CREATE INDEX IF NOT EXISTS ix_aegis_registration_state
                    ON aegis_registration (state);

                CREATE INDEX IF NOT EXISTS ix_aegis_registration_last_authenticated
                    ON aegis_registration (last_authenticated_at_utc);

                CREATE TABLE IF NOT EXISTS aegis_registration_audit
                (
                    audit_id bigserial PRIMARY KEY,
                    registration_id text NOT NULL,
                    event_index integer NOT NULL,
                    at_utc timestamptz NOT NULL,
                    action text NOT NULL,
                    actor text NOT NULL,
                    source_ip text NULL,
                    user_agent text NULL,
                    reason text NULL,
                    correlation_id text NULL,
                    previous_state text NULL,
                    resulting_state text NOT NULL,
                    CONSTRAINT ux_aegis_registration_audit_event
                        UNIQUE (registration_id, event_index)
                );

                CREATE INDEX IF NOT EXISTS ix_aegis_registration_audit_registration
                    ON aegis_registration_audit (registration_id, event_index);

                CREATE OR REPLACE FUNCTION aegis_registration_audit_immutable()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $audit$
                BEGIN
                    RAISE EXCEPTION 'aegis_registration_audit is append-only';
                END;
                $audit$;

                DROP TRIGGER IF EXISTS trg_aegis_registration_audit_immutable
                    ON aegis_registration_audit;

                CREATE TRIGGER trg_aegis_registration_audit_immutable
                    BEFORE UPDATE OR DELETE ON aegis_registration_audit
                    FOR EACH ROW
                    EXECUTE FUNCTION aegis_registration_audit_immutable();
                """,
                connection,migration);

            await command.ExecuteNonQueryAsync(ct);
            await migration.CommitAsync(ct);
            _initialized = true;
        }
        finally
        {
            _initializeGate.Release();
        }
    }

    private static LifecycleRegistrationRecord ReadBase(NpgsqlDataReader reader)
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
            [])
        {
            ConflictInstallationId = reader.IsDBNull(15) ? null : reader.GetString(15),
            ActiveRuntimeSessionId = reader.IsDBNull(16) ? null : reader.GetString(16),
            RuntimeLeaseExpiresAtUtc = reader.IsDBNull(17) ? null : reader.GetFieldValue<DateTimeOffset>(17),
            CredentialScopes=JsonSerializer.Deserialize<string[]>(reader.GetString(19)) ?? [],
            ConflictRuntimeSessionId = reader.IsDBNull(18) ? null : reader.GetString(18)
        };
    }

    private static async Task<List<LifecycleAuditEvent>> LoadAuditAsync(
        NpgsqlConnection connection,
        string registrationId,
        CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT at_utc, action, actor, source_ip, user_agent, reason,
                   correlation_id, previous_state, resulting_state
            FROM aegis_registration_audit
            WHERE registration_id = @registration_id
            ORDER BY event_index;
            """,
            connection);
        command.Parameters.AddWithValue("registration_id", registrationId);

        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(ct);
        var result = new List<LifecycleAuditEvent>();
        while (await reader.ReadAsync(ct))
        {
            LifecycleRegistrationState? previous = null;
            if (!reader.IsDBNull(7) &&
                Enum.TryParse(reader.GetString(7), true, out LifecycleRegistrationState parsedPrevious))
            {
                previous = parsedPrevious;
            }

            string resultingText = reader.GetString(8);
            if (!Enum.TryParse(resultingText, true, out LifecycleRegistrationState resulting))
                throw new InvalidOperationException(
                    $"Unknown audit resulting state '{resultingText}' for registration '{registrationId}'.");

            result.Add(new LifecycleAuditEvent(
                reader.GetFieldValue<DateTimeOffset>(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                previous,
                resulting));
        }

        return result;
    }

    private static async Task<IReadOnlyDictionary<string, List<LifecycleAuditEvent>>> LoadAuditMapAsync(
        NpgsqlConnection connection,
        CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT registration_id, event_index, at_utc, action, actor,
                   source_ip, user_agent, reason, correlation_id,
                   previous_state, resulting_state
            FROM aegis_registration_audit
            ORDER BY registration_id, event_index;
            """,
            connection);

        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(ct);
        var result = new Dictionary<string, List<LifecycleAuditEvent>>(StringComparer.Ordinal);

        while (await reader.ReadAsync(ct))
        {
            string registrationId = reader.GetString(0);

            LifecycleRegistrationState? previous = null;
            if (!reader.IsDBNull(9))
            {
                string previousText = reader.GetString(9);
                if (Enum.TryParse(previousText, true, out LifecycleRegistrationState parsedPrevious))
                    previous = parsedPrevious;
            }

            string resultingText = reader.GetString(10);
            if (!Enum.TryParse(resultingText, true, out LifecycleRegistrationState resulting))
                throw new InvalidOperationException(
                    $"Unknown audit resulting state '{resultingText}' for registration '{registrationId}'.");

            var audit = new LifecycleAuditEvent(
                reader.GetFieldValue<DateTimeOffset>(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetString(8),
                previous,
                resulting);

            if (!result.TryGetValue(registrationId, out List<LifecycleAuditEvent>? list))
            {
                list = [];
                result[registrationId] = list;
            }

            list.Add(audit);
        }

        return result;
    }
}
