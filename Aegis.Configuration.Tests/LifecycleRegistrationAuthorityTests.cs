using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;

public sealed class LifecycleRegistrationAuthorityTests
{
    [Fact]
    public async Task Introduction_IsZeroTrustPending_AndDuplicateIsIdempotent()
    {
        var store = new InMemoryLifecycleRegistrationStore();
        LifecycleRegistrationAuthority authority = CreateAuthority(store);

        LifecycleIntroductionResult first = await authority.IntroduceAsync(
            "Aegis.Hello",
            "Production",
            "install-1",
            new JsonObject { ["hostname"] = "kratos" },
            "unauthenticated-introduction",
            "10.10.0.10",
            "test",
            "corr-1",
            CancellationToken.None);

        LifecycleIntroductionResult second = await authority.IntroduceAsync(
            "Aegis.Hello",
            "Production",
            "install-1",
            null,
            "unauthenticated-introduction",
            "10.10.0.10",
            "test",
            "corr-2",
            CancellationToken.None);

        Assert.True(first.Created);
        Assert.False(first.Conflict);
        Assert.Equal(LifecycleRegistrationState.Pending, first.Record.State);
        Assert.NotNull(first.ClaimToken);
        Assert.Null(first.Record.CredentialHash);

        Assert.False(second.Created);
        Assert.False(second.Conflict);
        Assert.Equal(first.Record.RegistrationId, second.Record.RegistrationId);
        Assert.Null(second.ClaimToken);
        Assert.Single(await store.LoadAllAsync(CancellationToken.None));
    }

    [Fact]
    public async Task SameLogicalInstance_WithDifferentInstallationId_BecomesConflict()
    {
        var store = new InMemoryLifecycleRegistrationStore();
        LifecycleRegistrationAuthority authority = CreateAuthority(store);

        LifecycleIntroductionResult first = await authority.IntroduceAsync(
            "Aegis.Hello", "Production", "install-a", null,
            "intro", null, null, "corr-a", CancellationToken.None);

        LifecycleIntroductionResult conflicting = await authority.IntroduceAsync(
            "Aegis.Hello", "Production", "install-b", null,
            "intro", null, null, "corr-b", CancellationToken.None);

        Assert.True(conflicting.Conflict);
        Assert.False(conflicting.Created);
        Assert.Equal(first.Record.RegistrationId, conflicting.Record.RegistrationId);
        Assert.Equal("install-a", conflicting.Record.InstallationId);
        Assert.Equal("install-b", conflicting.Record.ConflictInstallationId);
        Assert.Equal(LifecycleRegistrationState.IdentityConflict, conflicting.Record.State);
        Assert.Contains(conflicting.Record.Audit, x => x.Action == "IdentityConflict");
    }

    [Fact]
    public async Task ApprovedClaim_IsSingleUse_AndCredentialIsStoredOnlyAsHash()
    {
        var store = new InMemoryLifecycleRegistrationStore();
        LifecycleRegistrationAuthority authority = CreateAuthority(store);

        LifecycleIntroductionResult introduction = await authority.IntroduceAsync(
            "Aegis.Hello", "Production", "install-1", null,
            "intro", null, null, "corr-1", CancellationToken.None);

        LifecycleRegistrationRecord? approved = await authority.DecideAsync(
            introduction.Record.RegistrationId,
            true,
            "administrator",
            "10.10.0.1",
            "browser",
            "approved for test",
            "corr-2",
            CancellationToken.None);

        Assert.NotNull(approved);
        Assert.Equal(LifecycleRegistrationState.Approved, approved!.State);

        LifecycleClaimResult claim = await authority.ClaimAsync(
            introduction.Record.RegistrationId,
            "Aegis.Hello",
            "Production",
            "install-1",
            introduction.ClaimToken!,
            "corr-3",
            CancellationToken.None);

        Assert.NotNull(claim.Credential);
        Assert.Equal(LifecycleRegistrationState.Registered, claim.Record!.State);
        Assert.NotEqual(claim.Credential, claim.Record.CredentialHash);
        Assert.StartsWith("PBKDF2-SHA256$", claim.Record.CredentialHash);

        LifecycleClaimResult replay = await authority.ClaimAsync(
            introduction.Record.RegistrationId,
            "Aegis.Hello",
            "Production",
            "install-1",
            introduction.ClaimToken!,
            "corr-4",
            CancellationToken.None);

        Assert.True(replay.InvalidClaim);
        Assert.Null(replay.Credential);
    }

    [Fact]
    public async Task CredentialRecovery_RequiresApproval_AndRotatesCredential()
    {
        var store = new InMemoryLifecycleRegistrationStore();
        LifecycleRegistrationAuthority authority = CreateAuthority(store);

        (LifecycleRegistrationRecord registered, string originalCredential) =
            await RegisterAsync(authority, "install-1");

        LifecycleIntroductionResult recovery = await authority.RequestRecoveryAsync(
            "Aegis.Hello",
            "Production",
            "install-1",
            "recovery-request",
            "10.10.0.10",
            "test",
            "corr-r1",
            CancellationToken.None);

        Assert.True(recovery.Created);
        Assert.Equal(LifecycleRegistrationState.RecoveryPending, recovery.Record.State);
        Assert.NotNull(recovery.ClaimToken);

        LifecycleClaimResult beforeApproval = await authority.ClaimAsync(
            registered.RegistrationId,
            "Aegis.Hello",
            "Production",
            "install-1",
            recovery.ClaimToken!,
            "corr-r2",
            CancellationToken.None);

        Assert.True(beforeApproval.Pending);
        Assert.Null(beforeApproval.Credential);

        LifecycleRegistrationRecord? recoveryApproved = await authority.DecideAsync(
            registered.RegistrationId,
            true,
            "administrator",
            null,
            null,
            "credential lost",
            "corr-r3",
            CancellationToken.None);

        Assert.Equal(LifecycleRegistrationState.RecoveryApproved, recoveryApproved!.State);
        Assert.Null(recoveryApproved.CredentialHash);

        LifecycleClaimResult replacement = await authority.ClaimAsync(
            registered.RegistrationId,
            "Aegis.Hello",
            "Production",
            "install-1",
            recovery.ClaimToken!,
            "corr-r4",
            CancellationToken.None);

        Assert.Equal(LifecycleRegistrationState.Registered, replacement.Record!.State);
        Assert.NotNull(replacement.Credential);
        Assert.NotEqual(originalCredential, replacement.Credential);
    }

    [Fact]
    public async Task Revocation_InvalidatesCredential_AndPurgePreservesHistory()
    {
        var store = new InMemoryLifecycleRegistrationStore();
        LifecycleRegistrationAuthority authority = CreateAuthority(store);

        (LifecycleRegistrationRecord registered, string credential) =
            await RegisterAsync(authority, "install-1");

        LifecycleRegistrationRecord? authenticated = await authority.AuthenticateAsync(
            "Aegis.Hello", "Production", "install-1", credential, "runtime-a", "corr-a", CancellationToken.None);
        Assert.Equal(LifecycleRegistrationState.Registered, authenticated!.State);

        LifecycleRegistrationRecord? revoked = await authority.RevokeAsync(
            registered.RegistrationId,
            "administrator",
            "10.10.0.1",
            "browser",
            "security test",
            "corr-r",
            CancellationToken.None);

        Assert.Equal(LifecycleRegistrationState.Revoked, revoked!.State);
        Assert.Null(revoked.CredentialHash);

        LifecycleRegistrationRecord? afterRevoke = await authority.AuthenticateAsync(
            "Aegis.Hello", "Production", "install-1", credential, "runtime-a", "corr-b", CancellationToken.None);
        Assert.Equal(LifecycleRegistrationState.Revoked, afterRevoke!.State);

        LifecycleRegistrationRecord? purged = await authority.PurgeAsync(
            registered.RegistrationId,
            "administrator",
            "10.10.0.1",
            "browser",
            "cleanup",
            "corr-p",
            CancellationToken.None);

        Assert.Equal(LifecycleRegistrationState.Purged, purged!.State);
        Assert.Empty(await authority.ListAsync(CancellationToken.None));

        IReadOnlyList<LifecycleRegistrationRecord> history =
            await authority.ListAllAsync(CancellationToken.None);
        Assert.Single(history);
        Assert.Equal(LifecycleRegistrationState.Purged, history[0].State);
        Assert.Contains(history[0].Audit, x => x.Action == "Revoked");
        Assert.Contains(history[0].Audit, x => x.Action == "Purged");
    }



    [Fact]
    public async Task AuthenticationWithWrongInstallationId_PersistsResolvableConflict()
    {
        var store = new InMemoryLifecycleRegistrationStore();
        LifecycleRegistrationAuthority authority = CreateAuthority(store);

        (LifecycleRegistrationRecord registered, string credential) =
            await RegisterAsync(authority, "install-original");

        LifecycleRegistrationRecord? conflict = await authority.AuthenticateAsync(
            "Aegis.Hello",
            "Production",
            "install-clone",
            credential,
            "runtime-clone",
            "corr-conflict",
            CancellationToken.None);

        Assert.NotNull(conflict);
        Assert.Equal(LifecycleRegistrationState.IdentityConflict, conflict!.State);
        Assert.Equal("install-clone", conflict.ConflictInstallationId);
        Assert.Contains(conflict.Audit, x => x.Action == "IdentityConflictAuthentication");

        LifecycleRegistrationRecord? persisted =
            await store.LoadAsync(registered.RegistrationId, CancellationToken.None);

        Assert.Equal(LifecycleRegistrationState.IdentityConflict, persisted!.State);
        Assert.Equal("install-clone", persisted.ConflictInstallationId);
    }

    [Fact]
    public async Task ReplaceIncomingConflict_RetiresOldIdentity_AndAllowsFreshPending()
    {
        var store = new InMemoryLifecycleRegistrationStore();
        LifecycleRegistrationAuthority authority = CreateAuthority(store);

        LifecycleIntroductionResult original = await authority.IntroduceAsync(
            "Aegis.Hello", "Production", "install-old", null,
            "intro", null, null, "corr-1", CancellationToken.None);

        LifecycleIntroductionResult conflict = await authority.IntroduceAsync(
            "Aegis.Hello", "Production", "install-new", null,
            "intro", null, null, "corr-2", CancellationToken.None);

        Assert.True(conflict.Conflict);
        Assert.Equal(LifecycleRegistrationState.IdentityConflict, conflict.Record.State);

        LifecycleRegistrationRecord? retired =
            await authority.ReplaceWithConflictingIdentityAsync(
                original.Record.RegistrationId,
                "administrator",
                "10.10.0.1",
                "browser",
                "replace machine",
                "corr-3",
                CancellationToken.None);

        Assert.Equal(LifecycleRegistrationState.Purged, retired!.State);
        Assert.Contains(
            retired.Audit,
            x => x.Action == "IdentityConflictResolvedReplaceWithIncoming");

        LifecycleIntroductionResult replacement = await authority.IntroduceAsync(
            "Aegis.Hello", "Production", "install-new", null,
            "intro", null, null, "corr-4", CancellationToken.None);

        Assert.True(replacement.Created);
        Assert.False(replacement.Conflict);
        Assert.Equal(LifecycleRegistrationState.Pending, replacement.Record.State);
        Assert.NotEqual(original.Record.RegistrationId, replacement.Record.RegistrationId);
        Assert.NotNull(replacement.ClaimToken);
    }


    [Fact]
    public async Task ConcurrentDuplicateIntroductions_ProduceOnePendingRegistration()
    {
        var store = new InMemoryLifecycleRegistrationStore();
        LifecycleRegistrationAuthority authority = CreateAuthority(store);

        Task<LifecycleIntroductionResult>[] attempts =
            Enumerable.Range(0, 12)
                .Select(i => authority.IntroduceAsync(
                    "Aegis.Hello",
                    "Production",
                    "install-concurrent",
                    new JsonObject { ["attempt"] = i },
                    "intro",
                    "10.10.0.10",
                    "test",
                    "corr-" + i,
                    CancellationToken.None))
                .ToArray();

        LifecycleIntroductionResult[] results = await Task.WhenAll(attempts);

        Assert.Single(results, x => x.Created);
        Assert.All(results, x => Assert.False(x.Conflict));
        Assert.Single(results.Select(x => x.Record.RegistrationId).Distinct());
        Assert.Single(await store.LoadAllAsync(CancellationToken.None));
        Assert.Equal(
            LifecycleRegistrationState.Pending,
            (await store.LoadAllAsync(CancellationToken.None)).Single().State);
    }

    [Fact]
    public async Task ConcurrentRuntimeSession_WithSameDurableIdentity_BecomesConflict()
    {
        var store = new InMemoryLifecycleRegistrationStore();
        LifecycleRegistrationAuthority authority = CreateAuthority(store);

        (LifecycleRegistrationRecord registered, string credential) =
            await RegisterAsync(authority, "install-runtime");

        LifecycleRegistrationRecord? first = await authority.AuthenticateAsync(
            "Aegis.Hello",
            "Production",
            "install-runtime",
            credential,
            "runtime-a",
            "corr-runtime-a",
            CancellationToken.None);

        Assert.NotNull(first);
        Assert.Equal(LifecycleRegistrationState.Registered, first!.State);
        Assert.Equal("runtime-a", first.ActiveRuntimeSessionId);
        Assert.True(first.RuntimeLeaseExpiresAtUtc > DateTimeOffset.UtcNow);

        LifecycleRegistrationRecord? conflict = await authority.AuthenticateAsync(
            "Aegis.Hello",
            "Production",
            "install-runtime",
            credential,
            "runtime-b",
            "corr-runtime-b",
            CancellationToken.None);

        Assert.NotNull(conflict);
        Assert.Equal(LifecycleRegistrationState.IdentityConflict, conflict!.State);
        Assert.Equal("runtime-a", conflict.ActiveRuntimeSessionId);
        Assert.Equal("runtime-b", conflict.ConflictRuntimeSessionId);
        Assert.Contains(conflict.Audit, x => x.Action == "ConcurrentRuntimeIdentityConflict");
    }

    [Fact]
    public async Task ExpiredApprovedClaim_IsRejectedWithoutIssuingCredential()
    {
        var store = new InMemoryLifecycleRegistrationStore();
        LifecycleRegistrationAuthority authority = CreateAuthority(store);

        LifecycleIntroductionResult introduction = await authority.IntroduceAsync(
            "Aegis.Hello",
            "Production",
            "install-expired",
            null,
            "intro",
            null,
            null,
            "corr-intro",
            CancellationToken.None);

        LifecycleRegistrationRecord? approved = await authority.DecideAsync(
            introduction.Record.RegistrationId,
            true,
            "administrator",
            null,
            null,
            "approve",
            "corr-approve",
            CancellationToken.None);

        Assert.NotNull(approved);

        LifecycleRegistrationRecord expired = approved! with
        {
            ClaimExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1)
        };
        await store.SaveAsync(expired, CancellationToken.None);

        LifecycleClaimResult claim = await authority.ClaimAsync(
            introduction.Record.RegistrationId,
            "Aegis.Hello",
            "Production",
            "install-expired",
            introduction.ClaimToken!,
            "corr-claim",
            CancellationToken.None);

        Assert.True(claim.InvalidClaim);
        Assert.Null(claim.Credential);

        LifecycleRegistrationRecord? persisted =
            await store.LoadAsync(introduction.Record.RegistrationId, CancellationToken.None);
        Assert.Equal(LifecycleRegistrationState.Approved, persisted!.State);
        Assert.Null(persisted.CredentialHash);
    }

    [Fact]
    public async Task InvalidDecisionState_IsRejected()
    {
        var store = new InMemoryLifecycleRegistrationStore();
        LifecycleRegistrationAuthority authority = CreateAuthority(store);

        (LifecycleRegistrationRecord registered, _) =
            await RegisterAsync(authority, "install-decision");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            authority.DecideAsync(
                registered.RegistrationId,
                true,
                "administrator",
                null,
                null,
                "invalid approve",
                "corr-invalid-approve",
                CancellationToken.None));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            authority.DecideAsync(
                registered.RegistrationId,
                false,
                "administrator",
                null,
                null,
                "invalid reject",
                "corr-invalid-reject",
                CancellationToken.None));
    }

    [Fact]
    public async Task RevokeRequiresRegisteredState()
    {
        var store = new InMemoryLifecycleRegistrationStore();
        LifecycleRegistrationAuthority authority = CreateAuthority(store);

        LifecycleIntroductionResult pending = await authority.IntroduceAsync(
            "Aegis.Hello",
            "Production",
            "install-pending-revoke",
            null,
            "intro",
            null,
            null,
            "corr-intro",
            CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            authority.RevokeAsync(
                pending.Record.RegistrationId,
                "administrator",
                null,
                null,
                "invalid revoke",
                "corr-invalid-revoke",
                CancellationToken.None));
    }

    [Fact]
    public async Task PurgeBeforeRevokeOrReject_IsRejected()
    {
        var store = new InMemoryLifecycleRegistrationStore();
        LifecycleRegistrationAuthority authority = CreateAuthority(store);

        LifecycleIntroductionResult introduction = await authority.IntroduceAsync(
            "Aegis.Hello", "Production", "install-1", null,
            "intro", null, null, "corr", CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            authority.PurgeAsync(
                introduction.Record.RegistrationId,
                "administrator",
                null,
                null,
                "invalid purge",
                "corr-p",
                CancellationToken.None));
    }

    private static LifecycleRegistrationAuthority CreateAuthority(
        ILifecycleRegistrationStore store)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AegisConfiguration:RegistrationClaimLifetimeMinutes"] = "30"
            })
            .Build();

        return new LifecycleRegistrationAuthority(configuration, store);
    }

    private static async Task<(LifecycleRegistrationRecord Record, string Credential)> RegisterAsync(
        LifecycleRegistrationAuthority authority,
        string installationId)
    {
        LifecycleIntroductionResult introduction = await authority.IntroduceAsync(
            "Aegis.Hello",
            "Production",
            installationId,
            null,
            "intro",
            null,
            null,
            "corr-intro",
            CancellationToken.None);

        await authority.DecideAsync(
            introduction.Record.RegistrationId,
            true,
            "administrator",
            null,
            null,
            "approved",
            "corr-approve",
            CancellationToken.None);

        LifecycleClaimResult claim = await authority.ClaimAsync(
            introduction.Record.RegistrationId,
            "Aegis.Hello",
            "Production",
            installationId,
            introduction.ClaimToken!,
            "corr-claim",
            CancellationToken.None);

        return (claim.Record!, claim.Credential!);
    }

    private sealed class InMemoryLifecycleRegistrationStore : ILifecycleRegistrationStore
    {
        private readonly Dictionary<string, LifecycleRegistrationRecord> _records =
            new(StringComparer.Ordinal);

        public Task<IReadOnlyList<LifecycleRegistrationRecord>> LoadAllAsync(CancellationToken ct)
            => Task.FromResult<IReadOnlyList<LifecycleRegistrationRecord>>(
                _records.Values.OrderByDescending(x => x.UpdatedAtUtc).ToList());

        public Task<LifecycleRegistrationRecord?> LoadAsync(
            string registrationId,
            CancellationToken ct)
        {
            _records.TryGetValue(registrationId, out LifecycleRegistrationRecord? record);
            return Task.FromResult(record);
        }

        public Task<LifecycleRegistrationRecord?> LoadActiveAsync(
            string applicationId,
            string instanceId,
            CancellationToken ct)
            => Task.FromResult(
                _records.Values
                    .Where(x => x.State != LifecycleRegistrationState.Purged)
                    .Where(x => string.Equals(x.ApplicationId, applicationId, StringComparison.OrdinalIgnoreCase))
                    .Where(x => string.Equals(x.InstanceId, instanceId, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(x => x.CreatedAtUtc)
                    .FirstOrDefault());

        public Task SaveAsync(LifecycleRegistrationRecord record, CancellationToken ct)
        {
            _records[record.RegistrationId] = record;
            return Task.CompletedTask;
        }
    }
}
