using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

internal enum LifecycleRegistrationState
{
    Pending,
    Approved,
    Registered,
    RecoveryPending,
    RecoveryApproved,
    Rejected,
    Revoked,
    IdentityConflict,
    Purged
}

internal sealed record LifecycleAuditEvent(
    DateTimeOffset AtUtc,
    string Action,
    string Actor,
    string? SourceIp,
    string? UserAgent,
    string? Reason,
    string? CorrelationId,
    LifecycleRegistrationState? PreviousState,
    LifecycleRegistrationState ResultingState);

internal sealed record LifecycleRegistrationRecord(
    string RegistrationId,
    string ApplicationId,
    string InstanceId,
    string InstallationId,
    LifecycleRegistrationState State,
    string ClaimTokenHash,
    DateTimeOffset ClaimExpiresAtUtc,
    string? CredentialHash,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? ApprovedAtUtc,
    DateTimeOffset? RegisteredAtUtc,
    DateTimeOffset? LastAuthenticatedAtUtc,
    DateTimeOffset? RevokedAtUtc,
    JsonObject? Metadata,
    List<LifecycleAuditEvent> Audit);

internal sealed record LifecycleIntroductionResult(
    LifecycleRegistrationRecord Record,
    string? ClaimToken,
    bool Created,
    bool Conflict);

internal sealed record LifecycleClaimResult(
    LifecycleRegistrationRecord? Record,
    string? Credential,
    bool Pending,
    bool Rejected,
    bool Conflict,
    bool InvalidClaim);

internal sealed class LifecycleRegistrationAuthority
{
    private readonly string _directory;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly TimeSpan _claimLifetime;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    public LifecycleRegistrationAuthority(IConfiguration configuration)
    {
        _directory = configuration["AegisConfiguration:RegistrationDirectory"]?.Trim()
            ?? "/var/lib/aegis/configuration/registrations";
        int minutes = configuration.GetValue<int?>("AegisConfiguration:RegistrationClaimLifetimeMinutes") ?? 30;
        _claimLifetime = TimeSpan.FromMinutes(Math.Clamp(minutes, 5, 1440));
        Directory.CreateDirectory(_directory);
    }

    public async Task<LifecycleIntroductionResult> IntroduceAsync(
        string applicationId,
        string instanceId,
        string installationId,
        JsonObject? metadata,
        string actor,
        string? sourceIp,
        string? userAgent,
        string? correlationId,
        CancellationToken ct)
    {
        Validate(applicationId, instanceId, installationId);
        await _gate.WaitAsync(ct);
        try
        {
            List<LifecycleRegistrationRecord> all = await LoadAllUnsafeAsync(ct);
            LifecycleRegistrationRecord? existing = all
                .Where(x => string.Equals(x.ApplicationId, applicationId, StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(x.InstanceId, instanceId, StringComparison.OrdinalIgnoreCase) &&
                            x.State != LifecycleRegistrationState.Purged)
                .OrderByDescending(x => x.CreatedAtUtc)
                .FirstOrDefault();

            if (existing is not null)
            {
                if (!string.Equals(existing.InstallationId, installationId, StringComparison.Ordinal))
                {
                    LifecycleRegistrationRecord conflict = existing with
                    {
                        State = LifecycleRegistrationState.IdentityConflict,
                        UpdatedAtUtc = DateTimeOffset.UtcNow,
                        Audit = [.. existing.Audit, Audit("IdentityConflict", actor, sourceIp, userAgent,
                            "Same ApplicationId/InstanceId presented a different InstallationId.", correlationId,
                            existing.State, LifecycleRegistrationState.IdentityConflict)]
                    };
                    await SaveUnsafeAsync(conflict, ct);
                    return new(conflict, null, false, true);
                }

                if (existing.State is LifecycleRegistrationState.Pending or LifecycleRegistrationState.Approved)
                    return new(existing, null, false, false);

                if (existing.State == LifecycleRegistrationState.Registered)
                    return new(existing, null, false, false);

                if (existing.State is LifecycleRegistrationState.Revoked or LifecycleRegistrationState.Rejected)
                {
                    // A revoked/rejected identity is not silently re-created. Administrator action is required.
                    return new(existing, null, false, false);
                }
            }

            string claim = Token();
            DateTimeOffset now = DateTimeOffset.UtcNow;
            LifecycleRegistrationRecord record = new(
                Guid.NewGuid().ToString("N"),
                applicationId.Trim(),
                instanceId.Trim(),
                installationId.Trim(),
                LifecycleRegistrationState.Pending,
                Hash(claim),
                now + _claimLifetime,
                null,
                now,
                now,
                null,
                null,
                null,
                null,
                metadata,
                [Audit("RegistrationRequested", actor, sourceIp, userAgent, null, correlationId, null, LifecycleRegistrationState.Pending)]);

            await SaveUnsafeAsync(record, ct);
            return new(record, claim, true, false);
        }
        finally { _gate.Release(); }
    }

    public async Task<LifecycleClaimResult> ClaimAsync(
        string registrationId,
        string applicationId,
        string instanceId,
        string installationId,
        string claimToken,
        string? correlationId,
        CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            LifecycleRegistrationRecord? record = await LoadUnsafeAsync(registrationId, ct);
            if (record is null)
                return new(null, null, false, false, false, true);

            if (!IdentityMatches(record, applicationId, instanceId, installationId))
                return new(record, null, false, false, true, false);

            if (!FixedEquals(record.ClaimTokenHash, claimToken) || DateTimeOffset.UtcNow > record.ClaimExpiresAtUtc)
                return new(record, null, false, false, false, true);

            if (record.State is LifecycleRegistrationState.Pending or LifecycleRegistrationState.RecoveryPending)
                return new(record, null, true, false, false, false);

            if (record.State == LifecycleRegistrationState.Rejected)
                return new(record, null, false, true, false, false);

            if (record.State == LifecycleRegistrationState.IdentityConflict)
                return new(record, null, false, false, true, false);

            if (record.State is not (LifecycleRegistrationState.Approved or LifecycleRegistrationState.RecoveryApproved))
                return new(record, null, false, false, false, true);

            string credential = Token();
            DateTimeOffset now = DateTimeOffset.UtcNow;
            LifecycleRegistrationRecord registered = record with
            {
                State = LifecycleRegistrationState.Registered,
                ClaimTokenHash = Hash(Token()),
                ClaimExpiresAtUtc = now,
                CredentialHash = Hash(credential),
                RegisteredAtUtc = now,
                UpdatedAtUtc = now,
                Audit = [.. record.Audit, Audit("CredentialClaimed", applicationId, null, null, null,
                    correlationId, record.State, LifecycleRegistrationState.Registered)]
            };
            await SaveUnsafeAsync(registered, ct);
            return new(registered, credential, false, false, false, false);
        }
        finally { _gate.Release(); }
    }

    public async Task<LifecycleRegistrationRecord?> AuthenticateAsync(
        string applicationId,
        string instanceId,
        string installationId,
        string credential,
        string? correlationId,
        CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            LifecycleRegistrationRecord? record = (await LoadAllUnsafeAsync(ct))
                .Where(x => string.Equals(x.ApplicationId, applicationId, StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(x.InstanceId, instanceId, StringComparison.OrdinalIgnoreCase) &&
                            x.State != LifecycleRegistrationState.Purged)
                .OrderByDescending(x => x.CreatedAtUtc)
                .FirstOrDefault();

            if (record is null || record.State != LifecycleRegistrationState.Registered)
                return record;
            if (!string.Equals(record.InstallationId, installationId, StringComparison.Ordinal))
                return record with { State = LifecycleRegistrationState.IdentityConflict };
            if (string.IsNullOrWhiteSpace(record.CredentialHash) || !FixedEquals(record.CredentialHash, credential))
                return null;

            LifecycleRegistrationRecord authenticated = record with
            {
                LastAuthenticatedAtUtc = DateTimeOffset.UtcNow,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };
            await SaveUnsafeAsync(authenticated, ct);
            return authenticated;
        }
        finally { _gate.Release(); }
    }

    public async Task<LifecycleIntroductionResult> RequestRecoveryAsync(
        string applicationId,
        string instanceId,
        string installationId,
        string actor,
        string? sourceIp,
        string? userAgent,
        string? correlationId,
        CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            LifecycleRegistrationRecord? record = (await LoadAllUnsafeAsync(ct))
                .Where(x => string.Equals(x.ApplicationId, applicationId, StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(x.InstanceId, instanceId, StringComparison.OrdinalIgnoreCase) &&
                            x.State != LifecycleRegistrationState.Purged)
                .OrderByDescending(x => x.CreatedAtUtc)
                .FirstOrDefault();

            if (record is null)
                throw new InvalidOperationException("No established registration exists for recovery.");
            if (!string.Equals(record.InstallationId, installationId, StringComparison.Ordinal))
                return new(record with { State = LifecycleRegistrationState.IdentityConflict }, null, false, true);

            if (record.State == LifecycleRegistrationState.RecoveryPending)
                return new(record, null, false, false);

            string claim = Token();
            DateTimeOffset now = DateTimeOffset.UtcNow;
            LifecycleRegistrationRecord pending = record with
            {
                State = LifecycleRegistrationState.RecoveryPending,
                ClaimTokenHash = Hash(claim),
                ClaimExpiresAtUtc = now + _claimLifetime,
                UpdatedAtUtc = now,
                Audit = [.. record.Audit, Audit("RecoveryRequested", actor, sourceIp, userAgent, null,
                    correlationId, record.State, LifecycleRegistrationState.RecoveryPending)]
            };
            await SaveUnsafeAsync(pending, ct);
            return new(pending, claim, true, false);
        }
        finally { _gate.Release(); }
    }

    public async Task<LifecycleRegistrationRecord?> DecideAsync(
        string registrationId,
        bool approve,
        string actor,
        string? sourceIp,
        string? userAgent,
        string? reason,
        string? correlationId,
        CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            LifecycleRegistrationRecord? record = await LoadUnsafeAsync(registrationId, ct);
            if (record is null) return null;

            LifecycleRegistrationState next = record.State switch
            {
                LifecycleRegistrationState.Pending when approve => LifecycleRegistrationState.Approved,
                LifecycleRegistrationState.RecoveryPending when approve => LifecycleRegistrationState.RecoveryApproved,
                LifecycleRegistrationState.Pending or LifecycleRegistrationState.RecoveryPending => LifecycleRegistrationState.Rejected,
                _ => record.State
            };

            if (next == record.State)
                return record;

            DateTimeOffset now = DateTimeOffset.UtcNow;
            LifecycleRegistrationRecord updated = record with
            {
                State = next,
                CredentialHash = next == LifecycleRegistrationState.RecoveryApproved ? null : record.CredentialHash,
                ApprovedAtUtc = approve ? now : record.ApprovedAtUtc,
                UpdatedAtUtc = now,
                Audit = [.. record.Audit, Audit(approve ? "Approved" : "Rejected", actor, sourceIp, userAgent,
                    reason, correlationId, record.State, next)]
            };
            await SaveUnsafeAsync(updated, ct);
            return updated;
        }
        finally { _gate.Release(); }
    }

    public async Task<IReadOnlyList<LifecycleRegistrationRecord>> ListAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            return (await LoadAllUnsafeAsync(ct))
                .Where(x => x.State != LifecycleRegistrationState.Purged)
                .OrderByDescending(x => x.UpdatedAtUtc)
                .ToList();
        }
        finally { _gate.Release(); }
    }

    private async Task<List<LifecycleRegistrationRecord>> LoadAllUnsafeAsync(CancellationToken ct)
    {
        var result = new List<LifecycleRegistrationRecord>();
        foreach (string file in Directory.EnumerateFiles(_directory, "*.json"))
        {
            await using FileStream stream = File.OpenRead(file);
            LifecycleRegistrationRecord? record =
                await JsonSerializer.DeserializeAsync<LifecycleRegistrationRecord>(stream, JsonOptions, ct);
            if (record is not null) result.Add(record);
        }
        return result;
    }

    private async Task<LifecycleRegistrationRecord?> LoadUnsafeAsync(string id, CancellationToken ct)
    {
        string path = Path.Combine(_directory, id + ".json");
        if (!File.Exists(path)) return null;
        await using FileStream stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<LifecycleRegistrationRecord>(stream, JsonOptions, ct);
    }

    private async Task SaveUnsafeAsync(LifecycleRegistrationRecord record, CancellationToken ct)
    {
        string path = Path.Combine(_directory, record.RegistrationId + ".json");
        string temp = path + ".tmp-" + Guid.NewGuid().ToString("N");
        await using (FileStream stream = File.Create(temp))
            await JsonSerializer.SerializeAsync(stream, record, JsonOptions, ct);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(temp, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        File.Move(temp, path, true);
    }

    private static string Token() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static bool FixedEquals(string hash, string value)
    {
        byte[] left = Convert.FromHexString(hash);
        byte[] right = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return CryptographicOperations.FixedTimeEquals(left, right);
    }
    private static bool IdentityMatches(LifecycleRegistrationRecord r, string app, string instance, string installation)
        => string.Equals(r.ApplicationId, app, StringComparison.OrdinalIgnoreCase) &&
           string.Equals(r.InstanceId, instance, StringComparison.OrdinalIgnoreCase) &&
           string.Equals(r.InstallationId, installation, StringComparison.Ordinal);
    private static void Validate(string app, string instance, string installation)
    {
        if (string.IsNullOrWhiteSpace(app) || string.IsNullOrWhiteSpace(instance) || string.IsNullOrWhiteSpace(installation))
            throw new ArgumentException("ApplicationId, InstanceId and InstallationId are required.");
        if (app.Length > 160 || instance.Length > 160 || installation.Length > 256)
            throw new ArgumentException("Registration identity exceeds the permitted length.");
    }
    private static LifecycleAuditEvent Audit(string action, string actor, string? ip, string? agent, string? reason,
        string? correlation, LifecycleRegistrationState? previous, LifecycleRegistrationState resulting)
        => new(DateTimeOffset.UtcNow, action, actor, ip, agent, reason, correlation, previous, resulting);
}
