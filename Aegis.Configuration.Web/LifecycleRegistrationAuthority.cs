using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Npgsql;

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
    List<LifecycleAuditEvent> Audit)
{
    public string? ConflictInstallationId { get; init; }
}

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
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly TimeSpan _claimLifetime;
    private readonly PostgresLifecycleRegistrationStore _store;

    public LifecycleRegistrationAuthority(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        int minutes =
            configuration.GetValue<int?>("AegisConfiguration:RegistrationClaimLifetimeMinutes")
            ?? 30;

        _claimLifetime = TimeSpan.FromMinutes(Math.Clamp(minutes, 5, 1440));
        _store = new PostgresLifecycleRegistrationStore(configuration);
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
            LifecycleRegistrationRecord? existing =
                await FindActiveAsync(applicationId, instanceId, ct);

            if (existing is not null)
            {
                if (!string.Equals(existing.InstallationId, installationId, StringComparison.Ordinal))
                {
                    LifecycleRegistrationRecord conflict = existing with
                    {
                        State = LifecycleRegistrationState.IdentityConflict,
                        ConflictInstallationId = installationId,
                        UpdatedAtUtc = DateTimeOffset.UtcNow,
                        Audit =
                        [
                            .. existing.Audit,
                            Audit(
                                "IdentityConflict",
                                actor,
                                sourceIp,
                                userAgent,
                                "Same ApplicationId/InstanceId presented a different InstallationId.",
                                correlationId,
                                existing.State,
                                LifecycleRegistrationState.IdentityConflict)
                        ]
                    };

                    await _store.SaveAsync(conflict, ct);
                    return new(conflict, null, false, true);
                }

                return new(
                    existing,
                    null,
                    false,
                    existing.State == LifecycleRegistrationState.IdentityConflict);
            }

            string claim = Token();
            DateTimeOffset now = DateTimeOffset.UtcNow;

            var record = new LifecycleRegistrationRecord(
                Guid.NewGuid().ToString("N"),
                applicationId.Trim(),
                instanceId.Trim(),
                installationId.Trim(),
                LifecycleRegistrationState.Pending,
                HashToken(claim),
                now + _claimLifetime,
                null,
                now,
                now,
                null,
                null,
                null,
                null,
                metadata,
                [
                    Audit(
                        "RegistrationRequested",
                        actor,
                        sourceIp,
                        userAgent,
                        null,
                        correlationId,
                        null,
                        LifecycleRegistrationState.Pending)
                ]);

            try
            {
                await _store.SaveAsync(record, ct);
                return new(record, claim, true, false);
            }
            catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
            {
                LifecycleRegistrationRecord? authoritative =
                    await FindActiveAsync(applicationId, instanceId, ct);

                if (authoritative is null)
                    throw;

                bool conflict =
                    !string.Equals(
                        authoritative.InstallationId,
                        installationId,
                        StringComparison.Ordinal);

                return new(authoritative, null, false, conflict);
            }
        }
        finally
        {
            _gate.Release();
        }
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
            LifecycleRegistrationRecord? record =
                await _store.LoadAsync(registrationId, ct);

            if (record is null)
                return new(null, null, false, false, false, true);

            if (!IdentityMatches(record, applicationId, instanceId, installationId))
                return new(record, null, false, false, true, false);

            if (!VerifyTokenHash(record.ClaimTokenHash, claimToken) ||
                DateTimeOffset.UtcNow > record.ClaimExpiresAtUtc)
            {
                return new(record, null, false, false, false, true);
            }

            if (record.State is
                LifecycleRegistrationState.Pending or
                LifecycleRegistrationState.RecoveryPending)
            {
                return new(record, null, true, false, false, false);
            }

            if (record.State == LifecycleRegistrationState.Rejected)
                return new(record, null, false, true, false, false);

            if (record.State == LifecycleRegistrationState.IdentityConflict)
                return new(record, null, false, false, true, false);

            if (record.State is not
                (LifecycleRegistrationState.Approved or
                 LifecycleRegistrationState.RecoveryApproved))
            {
                return new(record, null, false, false, false, true);
            }

            string credential = Token();
            DateTimeOffset now = DateTimeOffset.UtcNow;

            LifecycleRegistrationRecord registered = record with
            {
                State = LifecycleRegistrationState.Registered,
                ClaimTokenHash = HashToken(Token()),
                ClaimExpiresAtUtc = now,
                CredentialHash = HashCredential(credential),
                RegisteredAtUtc = now,
                UpdatedAtUtc = now,
                Audit =
                [
                    .. record.Audit,
                    Audit(
                        "CredentialClaimed",
                        applicationId,
                        null,
                        null,
                        null,
                        correlationId,
                        record.State,
                        LifecycleRegistrationState.Registered)
                ]
            };

            await _store.SaveAsync(registered, ct);

            return new(
                registered,
                credential,
                false,
                false,
                false,
                false);
        }
        finally
        {
            _gate.Release();
        }
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
            LifecycleRegistrationRecord? record =
                await FindActiveAsync(applicationId, instanceId, ct);

            if (record is null)
                return null;

            if (!string.Equals(
                    record.InstallationId,
                    installationId,
                    StringComparison.Ordinal))
            {
                return record with
                {
                    State = LifecycleRegistrationState.IdentityConflict,
                    ConflictInstallationId = installationId
                };
            }

            if (record.State == LifecycleRegistrationState.Revoked)
                return record;

            if (record.State != LifecycleRegistrationState.Registered)
                return null;

            if (string.IsNullOrWhiteSpace(record.CredentialHash) ||
                !VerifyCredentialHash(record.CredentialHash, credential))
            {
                return null;
            }

            LifecycleRegistrationRecord authenticated = record with
            {
                LastAuthenticatedAtUtc = DateTimeOffset.UtcNow,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };

            await _store.SaveAsync(authenticated, ct);
            return authenticated;
        }
        finally
        {
            _gate.Release();
        }
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
            LifecycleRegistrationRecord? record =
                await FindActiveAsync(applicationId, instanceId, ct);

            if (record is null)
                throw new InvalidOperationException(
                    "No established registration exists for recovery.");

            if (!string.Equals(
                    record.InstallationId,
                    installationId,
                    StringComparison.Ordinal))
            {
                return new(
                    record with
                    {
                        State = LifecycleRegistrationState.IdentityConflict,
                        ConflictInstallationId = installationId
                    },
                    null,
                    false,
                    true);
            }

            if (record.State == LifecycleRegistrationState.RecoveryPending)
                return new(record, null, false, false);

            string claim = Token();
            DateTimeOffset now = DateTimeOffset.UtcNow;

            LifecycleRegistrationRecord pending = record with
            {
                State = LifecycleRegistrationState.RecoveryPending,
                ClaimTokenHash = HashToken(claim),
                ClaimExpiresAtUtc = now + _claimLifetime,
                UpdatedAtUtc = now,
                Audit =
                [
                    .. record.Audit,
                    Audit(
                        "RecoveryRequested",
                        actor,
                        sourceIp,
                        userAgent,
                        null,
                        correlationId,
                        record.State,
                        LifecycleRegistrationState.RecoveryPending)
                ]
            };

            await _store.SaveAsync(pending, ct);

            return new(
                pending,
                claim,
                true,
                false);
        }
        finally
        {
            _gate.Release();
        }
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
            LifecycleRegistrationRecord? record =
                await _store.LoadAsync(registrationId, ct);

            if (record is null)
                return null;

            LifecycleRegistrationState next = record.State switch
            {
                LifecycleRegistrationState.Pending when approve
                    => LifecycleRegistrationState.Approved,

                LifecycleRegistrationState.RecoveryPending when approve
                    => LifecycleRegistrationState.RecoveryApproved,

                LifecycleRegistrationState.Pending or
                LifecycleRegistrationState.RecoveryPending
                    => LifecycleRegistrationState.Rejected,

                _ => record.State
            };

            if (next == record.State)
                return record;

            DateTimeOffset now = DateTimeOffset.UtcNow;

            LifecycleRegistrationRecord updated = record with
            {
                State = next,
                CredentialHash =
                    next == LifecycleRegistrationState.RecoveryApproved
                        ? null
                        : record.CredentialHash,
                ApprovedAtUtc = approve ? now : record.ApprovedAtUtc,
                UpdatedAtUtc = now,
                Audit =
                [
                    .. record.Audit,
                    Audit(
                        approve ? "Approved" : "Rejected",
                        actor,
                        sourceIp,
                        userAgent,
                        reason,
                        correlationId,
                        record.State,
                        next)
                ]
            };

            await _store.SaveAsync(updated, ct);
            return updated;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<LifecycleRegistrationRecord?> RevokeAsync(
        string registrationId,
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
            LifecycleRegistrationRecord? record =
                await _store.LoadAsync(registrationId, ct);

            if (record is null)
                return null;

            if (record.State == LifecycleRegistrationState.Purged)
                return record;

            DateTimeOffset now = DateTimeOffset.UtcNow;

            LifecycleRegistrationRecord revoked = record with
            {
                State = LifecycleRegistrationState.Revoked,
                CredentialHash = null,
                ClaimTokenHash = HashToken(Token()),
                ClaimExpiresAtUtc = now,
                RevokedAtUtc = now,
                UpdatedAtUtc = now,
                Audit =
                [
                    .. record.Audit,
                    Audit(
                        "Revoked",
                        actor,
                        sourceIp,
                        userAgent,
                        reason,
                        correlationId,
                        record.State,
                        LifecycleRegistrationState.Revoked)
                ]
            };

            await _store.SaveAsync(revoked, ct);
            return revoked;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<LifecycleRegistrationRecord?> PurgeAsync(
        string registrationId,
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
            LifecycleRegistrationRecord? record =
                await _store.LoadAsync(registrationId, ct);

            if (record is null)
                return null;

            if (record.State is not
                (LifecycleRegistrationState.Revoked or
                 LifecycleRegistrationState.Rejected))
            {
                throw new InvalidOperationException(
                    "Registration must be revoked or rejected before purge.");
            }

            DateTimeOffset now = DateTimeOffset.UtcNow;

            LifecycleRegistrationRecord purged = record with
            {
                State = LifecycleRegistrationState.Purged,
                CredentialHash = null,
                ClaimTokenHash = HashToken(Token()),
                ClaimExpiresAtUtc = now,
                UpdatedAtUtc = now,
                ConflictInstallationId = null,
                Audit =
                [
                    .. record.Audit,
                    Audit(
                        "Purged",
                        actor,
                        sourceIp,
                        userAgent,
                        reason,
                        correlationId,
                        record.State,
                        LifecycleRegistrationState.Purged)
                ]
            };

            await _store.SaveAsync(purged, ct);
            return purged;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<LifecycleRegistrationRecord?> RetainExistingIdentityAsync(
        string registrationId,
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
            LifecycleRegistrationRecord? record =
                await _store.LoadAsync(registrationId, ct);

            if (record is null)
                return null;

            if (record.State != LifecycleRegistrationState.IdentityConflict)
            {
                throw new InvalidOperationException(
                    "Registration is not in identity conflict.");
            }

            LifecycleRegistrationState restored =
                !string.IsNullOrWhiteSpace(record.CredentialHash) &&
                record.RegisteredAtUtc.HasValue
                    ? LifecycleRegistrationState.Registered
                    : LifecycleRegistrationState.Pending;

            LifecycleRegistrationRecord resolved = record with
            {
                State = restored,
                ConflictInstallationId = null,
                UpdatedAtUtc = DateTimeOffset.UtcNow,
                Audit =
                [
                    .. record.Audit,
                    Audit(
                        "IdentityConflictResolvedRetainExisting",
                        actor,
                        sourceIp,
                        userAgent,
                        reason,
                        correlationId,
                        record.State,
                        restored)
                ]
            };

            await _store.SaveAsync(resolved, ct);
            return resolved;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<LifecycleRegistrationRecord>> ListAsync(
        CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            return (await _store.LoadAllAsync(ct))
                .Where(x => x.State != LifecycleRegistrationState.Purged)
                .OrderByDescending(x => x.UpdatedAtUtc)
                .ToList();
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<LifecycleRegistrationRecord?> FindActiveAsync(
        string applicationId,
        string instanceId,
        CancellationToken ct)
    {
        return (await _store.LoadAllAsync(ct))
            .Where(x =>
                x.State != LifecycleRegistrationState.Purged &&
                string.Equals(
                    x.ApplicationId,
                    applicationId,
                    StringComparison.OrdinalIgnoreCase) &&
                string.Equals(
                    x.InstanceId,
                    instanceId,
                    StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefault();
    }

    private static string Token()
        => Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));

    private static string HashToken(string value)
        => Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();

    private static bool VerifyTokenHash(string hash, string value)
    {
        try
        {
            byte[] expected = Convert.FromHexString(hash);
            byte[] actual =
                SHA256.HashData(Encoding.UTF8.GetBytes(value));

            return CryptographicOperations.FixedTimeEquals(
                expected,
                actual);
        }
        catch
        {
            return false;
        }
    }

    private static string HashCredential(string credential)
    {
        const int iterations = 210_000;

        byte[] salt = RandomNumberGenerator.GetBytes(32);
        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(
            credential,
            salt,
            iterations,
            HashAlgorithmName.SHA256,
            32);

        return "PBKDF2-SHA256$" +
               iterations +
               "$" +
               Convert.ToBase64String(salt) +
               "$" +
               Convert.ToBase64String(hash);
    }

    private static bool VerifyCredentialHash(
        string encoded,
        string credential)
    {
        try
        {
            string[] parts = encoded.Split('$');

            if (parts.Length != 4 ||
                !string.Equals(
                    parts[0],
                    "PBKDF2-SHA256",
                    StringComparison.Ordinal))
            {
                return false;
            }

            int iterations = int.Parse(
                parts[1],
                System.Globalization.CultureInfo.InvariantCulture);

            byte[] salt = Convert.FromBase64String(parts[2]);
            byte[] expected = Convert.FromBase64String(parts[3]);

            byte[] actual = Rfc2898DeriveBytes.Pbkdf2(
                credential,
                salt,
                iterations,
                HashAlgorithmName.SHA256,
                expected.Length);

            return CryptographicOperations.FixedTimeEquals(
                expected,
                actual);
        }
        catch
        {
            return false;
        }
    }

    private static bool IdentityMatches(
        LifecycleRegistrationRecord record,
        string applicationId,
        string instanceId,
        string installationId)
    {
        return
            string.Equals(
                record.ApplicationId,
                applicationId,
                StringComparison.OrdinalIgnoreCase) &&
            string.Equals(
                record.InstanceId,
                instanceId,
                StringComparison.OrdinalIgnoreCase) &&
            string.Equals(
                record.InstallationId,
                installationId,
                StringComparison.Ordinal);
    }

    private static void Validate(
        string applicationId,
        string instanceId,
        string installationId)
    {
        if (string.IsNullOrWhiteSpace(applicationId) ||
            string.IsNullOrWhiteSpace(instanceId) ||
            string.IsNullOrWhiteSpace(installationId))
        {
            throw new ArgumentException(
                "ApplicationId, InstanceId and InstallationId are required.");
        }

        if (applicationId.Length > 160 ||
            instanceId.Length > 160 ||
            installationId.Length > 256)
        {
            throw new ArgumentException(
                "Registration identity exceeds the permitted length.");
        }
    }

    private static LifecycleAuditEvent Audit(
        string action,
        string actor,
        string? sourceIp,
        string? userAgent,
        string? reason,
        string? correlationId,
        LifecycleRegistrationState? previousState,
        LifecycleRegistrationState resultingState)
    {
        return new LifecycleAuditEvent(
            DateTimeOffset.UtcNow,
            action,
            actor,
            sourceIp,
            userAgent,
            reason,
            correlationId,
            previousState,
            resultingState);
    }
}
