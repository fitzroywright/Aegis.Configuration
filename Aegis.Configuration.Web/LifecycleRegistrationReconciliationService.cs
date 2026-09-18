internal sealed record RegistrationReconciliationIssue(
    string RegistrationId,
    string ApplicationId,
    string InstanceId,
    string State,
    string Severity,
    string Code,
    string Message,
    DateTimeOffset DetectedAtUtc);

internal sealed record RegistrationReconciliationSnapshot(
    DateTimeOffset CheckedAtUtc,
    int RegistrationCount,
    int HealthyCount,
    IReadOnlyList<RegistrationReconciliationIssue> Issues)
{
    public bool Healthy => Issues.Count == 0;
}

internal sealed class RegistrationReconciliationState
{
    private readonly object _gate = new();
    private RegistrationReconciliationSnapshot _snapshot =
        new(DateTimeOffset.MinValue, 0, 0, []);

    public void Set(RegistrationReconciliationSnapshot snapshot)
    {
        lock (_gate) _snapshot = snapshot;
    }

    public RegistrationReconciliationSnapshot Get()
    {
        lock (_gate) return _snapshot;
    }
}

internal sealed class LifecycleRegistrationReconciliationService(
    LifecycleRegistrationAuthority authority,
    RegistrationReconciliationState state,
    IConfiguration configuration,
    ILogger<LifecycleRegistrationReconciliationService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        int intervalSeconds = Math.Clamp(
            configuration.GetValue<int?>("AegisConfiguration:RegistrationReconciliationIntervalSeconds") ?? 60,
            30,
            3600);
        int staleMinutes = Math.Clamp(
            configuration.GetValue<int?>("AegisConfiguration:RegistrationStaleAfterMinutes") ?? 15,
            5,
            10080);
        int pendingMinutes = Math.Clamp(
            configuration.GetValue<int?>("AegisConfiguration:RegistrationPendingStaleAfterMinutes") ?? 60,
            10,
            10080);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                RegistrationReconciliationSnapshot snapshot =
                    await BuildSnapshotAsync(
                        TimeSpan.FromMinutes(staleMinutes),
                        TimeSpan.FromMinutes(pendingMinutes),
                        stoppingToken);

                state.Set(snapshot);

                if (snapshot.Healthy)
                {
                    logger.LogInformation(
                        "Registration reconciliation healthy. Registrations={RegistrationCount}.",
                        snapshot.RegistrationCount);
                }
                else
                {
                    logger.LogWarning(
                        "Registration reconciliation found {IssueCount} issue(s) across {RegistrationCount} registrations.",
                        snapshot.Issues.Count,
                        snapshot.RegistrationCount);

                    foreach (RegistrationReconciliationIssue issue in snapshot.Issues)
                    {
                        logger.LogWarning(
                            "Registration reconciliation issue {Code}: {ApplicationId}/{InstanceId} {State} - {Message}",
                            issue.Code,
                            issue.ApplicationId,
                            issue.InstanceId,
                            issue.State,
                            issue.Message);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Registration reconciliation failed.");
                state.Set(new RegistrationReconciliationSnapshot(
                    DateTimeOffset.UtcNow,
                    0,
                    0,
                    [
                        new RegistrationReconciliationIssue(
                            "control-plane",
                            "Aegis.Configuration",
                            Environment.MachineName,
                            "Error",
                            "Error",
                            "RECONCILIATION_FAILED",
                            ex.Message,
                            DateTimeOffset.UtcNow)
                    ]));
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(intervalSeconds), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
        }
    }

    private async Task<RegistrationReconciliationSnapshot> BuildSnapshotAsync(
        TimeSpan staleAfter,
        TimeSpan pendingStaleAfter,
        CancellationToken ct)
    {
        IReadOnlyList<LifecycleRegistrationRecord> registrations =
            await authority.ListAsync(ct);

        DateTimeOffset now = DateTimeOffset.UtcNow;
        var issues = new List<RegistrationReconciliationIssue>();
        int healthy = 0;

        foreach (LifecycleRegistrationRecord record in registrations)
        {
            RegistrationReconciliationIssue? issue = record.State switch
            {
                LifecycleRegistrationState.Registered
                    when !record.LastAuthenticatedAtUtc.HasValue =>
                    Issue(record, "Warning", "NEVER_AUTHENTICATED",
                        "Registration is marked Registered but has never authenticated.", now),

                LifecycleRegistrationState.Registered
                    when now - record.LastAuthenticatedAtUtc!.Value > staleAfter =>
                    Issue(record, "Warning", "AUTHENTICATION_STALE",
                        $"No successful authentication has been observed for {(now - record.LastAuthenticatedAtUtc.Value).TotalMinutes:F0} minutes.", now),

                LifecycleRegistrationState.Pending
                    when now - record.CreatedAtUtc > pendingStaleAfter =>
                    Issue(record, "Warning", "PENDING_STALE",
                        "Pending registration has exceeded the expected approval window.", now),

                LifecycleRegistrationState.RecoveryPending
                    when now - record.UpdatedAtUtc > pendingStaleAfter =>
                    Issue(record, "Warning", "RECOVERY_STALE",
                        "Credential recovery has remained pending beyond the expected window.", now),

                LifecycleRegistrationState.IdentityConflict =>
                    Issue(record, "Error", "IDENTITY_CONFLICT",
                        "A different InstallationId has presented the same ApplicationId/InstanceId.", now),

                LifecycleRegistrationState.Approved
                    when now > record.ClaimExpiresAtUtc =>
                    Issue(record, "Error", "APPROVED_CLAIM_EXPIRED",
                        "Registration was approved but the one-time claim expired before credential delivery.", now),

                LifecycleRegistrationState.RecoveryApproved
                    when now > record.ClaimExpiresAtUtc =>
                    Issue(record, "Error", "RECOVERY_CLAIM_EXPIRED",
                        "Recovery was approved but the replacement credential claim expired.", now),

                _ => null
            };

            if (issue is null) healthy++;
            else issues.Add(issue);
        }

        return new RegistrationReconciliationSnapshot(
            now,
            registrations.Count,
            healthy,
            issues);
    }

    private static RegistrationReconciliationIssue Issue(
        LifecycleRegistrationRecord record,
        string severity,
        string code,
        string message,
        DateTimeOffset detectedAtUtc)
        => new(
            record.RegistrationId,
            record.ApplicationId,
            record.InstanceId,
            record.State.ToString(),
            severity,
            code,
            message,
            detectedAtUtc);
}
