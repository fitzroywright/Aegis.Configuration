public sealed class LifecycleFlowSnapshotTests
{
    [Fact]
    public void PendingJourney_BecomesSlowThenStalledByStageAge()
    {
        DateTimeOffset now = new(2026, 9, 18, 23, 0, 0, TimeSpan.Zero);

        LifecycleRegistrationRecord slowRecord = Record(
            "reg-slow",
            LifecycleRegistrationState.Pending,
            now.AddSeconds(-400),
            [
                Audit(now.AddSeconds(-400), "RegistrationRequested", LifecycleRegistrationState.Pending)
            ]);

        LifecycleRegistrationRecord stalledRecord = Record(
            "reg-stalled",
            LifecycleRegistrationState.Pending,
            now.AddSeconds(-1000),
            [
                Audit(now.AddSeconds(-1000), "RegistrationRequested", LifecycleRegistrationState.Pending)
            ]);

        LifecycleFlowSnapshot snapshot =
            LifecycleFlowSnapshotBuilder.Build([slowRecord, stalledRecord], 50, now);

        LifecycleFlowJourneyView slow =
            snapshot.Journeys.Single(x => x.RegistrationId == "reg-slow");
        LifecycleFlowJourneyView stalled =
            snapshot.Journeys.Single(x => x.RegistrationId == "reg-stalled");

        Assert.Equal("Approval", slow.CurrentStage);
        Assert.Equal("Slow", slow.Health);
        Assert.Equal("Stalled", stalled.Health);
    }

    [Fact]
    public void IdentityConflict_IsFailedAndPrioritized()
    {
        DateTimeOffset now = new(2026, 9, 18, 23, 0, 0, TimeSpan.Zero);

        LifecycleRegistrationRecord healthy = Record(
            "reg-ok",
            LifecycleRegistrationState.Registered,
            now.AddMinutes(-2),
            [
                Audit(now.AddMinutes(-2), "Authenticated", LifecycleRegistrationState.Registered)
            ],
            lastAuthenticatedAtUtc: now.AddMinutes(-2));

        LifecycleRegistrationRecord conflict = Record(
            "reg-conflict",
            LifecycleRegistrationState.IdentityConflict,
            now.AddSeconds(-30),
            [
                Audit(now.AddSeconds(-30), "IdentityConflictAuthentication", LifecycleRegistrationState.IdentityConflict)
            ]);

        LifecycleFlowSnapshot snapshot =
            LifecycleFlowSnapshotBuilder.Build([healthy, conflict], 50, now);

        Assert.Equal("reg-conflict", snapshot.Journeys[0].RegistrationId);
        Assert.Equal("Failed", snapshot.Journeys[0].Health);
        Assert.Equal("Identity Conflict", snapshot.Journeys[0].CurrentStage);
        Assert.Equal("Healthy", snapshot.Journeys[1].Health);
    }

    [Fact]
    public void FlowEvents_MapLifecycleActionsToRealStagesAndCorrelationIds()
    {
        DateTimeOffset now = new(2026, 9, 18, 23, 0, 0, TimeSpan.Zero);

        LifecycleRegistrationRecord record = Record(
            "reg-flow",
            LifecycleRegistrationState.Registered,
            now.AddMinutes(-1),
            [
                new LifecycleAuditEvent(
                    now.AddMinutes(-3),
                    "RegistrationRequested",
                    "intro",
                    null,
                    null,
                    null,
                    "corr-request",
                    null,
                    LifecycleRegistrationState.Pending),
                new LifecycleAuditEvent(
                    now.AddMinutes(-2),
                    "CredentialClaimed",
                    "Aegis.Hello",
                    null,
                    null,
                    null,
                    "corr-claim",
                    LifecycleRegistrationState.Approved,
                    LifecycleRegistrationState.Registered),
                new LifecycleAuditEvent(
                    now.AddMinutes(-1),
                    "ConfigurationContractPublished",
                    "Aegis.Hello",
                    null,
                    null,
                    "accepted",
                    "corr-publish",
                    LifecycleRegistrationState.Registered,
                    LifecycleRegistrationState.Registered)
            ],
            lastAuthenticatedAtUtc: now.AddMinutes(-1));

        LifecycleFlowSnapshot snapshot =
            LifecycleFlowSnapshotBuilder.Build([record], 50, now);

        Assert.Contains(snapshot.Events, x =>
            x.Stage == "Request" &&
            x.Outcome == "Started" &&
            x.CorrelationId == "corr-request");

        Assert.Contains(snapshot.Events, x =>
            x.Stage == "Claim" &&
            x.Outcome == "Succeeded" &&
            x.CorrelationId == "corr-claim");

        Assert.Contains(snapshot.Events, x =>
            x.Stage == "Publish" &&
            x.Outcome == "Succeeded" &&
            x.CorrelationId == "corr-publish");
    }

    [Fact]
    public void ContractRejection_MarksRegisteredJourneyFailed()
    {
        DateTimeOffset now = new(2026, 9, 18, 23, 0, 0, TimeSpan.Zero);

        LifecycleRegistrationRecord record = Record(
            "reg-rejected-contract",
            LifecycleRegistrationState.Registered,
            now.AddSeconds(-5),
            [
                Audit(
                    now.AddSeconds(-20),
                    "ConfigurationContractRejected",
                    LifecycleRegistrationState.Registered),
                Audit(
                    now.AddSeconds(-5),
                    "Authenticated",
                    LifecycleRegistrationState.Registered)
            ],
            lastAuthenticatedAtUtc: now.AddSeconds(-5));

        LifecycleFlowSnapshot snapshot =
            LifecycleFlowSnapshotBuilder.Build([record], 50, now);

        Assert.Equal("Failed", snapshot.Journeys.Single().Health);
    }

    private static LifecycleRegistrationRecord Record(
        string id,
        LifecycleRegistrationState state,
        DateTimeOffset updatedAtUtc,
        List<LifecycleAuditEvent> audit,
        DateTimeOffset? lastAuthenticatedAtUtc = null)
        => new(
            id,
            "Aegis.Hello",
            "Production",
            "install-1",
            state,
            "claim-hash",
            updatedAtUtc.AddMinutes(30),
            state == LifecycleRegistrationState.Registered ? "PBKDF2-SHA256$1$c2FsdA==$aGFzaA==" : null,
            updatedAtUtc.AddMinutes(-1),
            updatedAtUtc,
            null,
            state == LifecycleRegistrationState.Registered ? updatedAtUtc.AddMinutes(-1) : null,
            lastAuthenticatedAtUtc,
            null,
            null,
            audit);

    private static LifecycleAuditEvent Audit(
        DateTimeOffset at,
        string action,
        LifecycleRegistrationState resultingState)
        => new(
            at,
            action,
            "test",
            null,
            null,
            null,
            "corr-" + action,
            null,
            resultingState);
}
