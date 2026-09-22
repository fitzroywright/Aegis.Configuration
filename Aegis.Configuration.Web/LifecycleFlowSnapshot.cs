internal sealed record LifecycleFlowEventView(
    DateTimeOffset AtUtc,
    string RegistrationId,
    string ApplicationId,
    string InstanceId,
    string InstallationId,
    string Stage,
    string Outcome,
    string Action,
    string Actor,
    string? CorrelationId,
    string? Reason,
    string? PreviousState,
    string ResultingState);

internal sealed record LifecycleFlowJourneyView(
    string RegistrationId,
    string ApplicationId,
    string InstanceId,
    string InstallationId,
    string State,
    string CurrentStage,
    string Health,
    long StageAgeSeconds,
    DateTimeOffset StageEnteredAtUtc,
    DateTimeOffset? LastEventAtUtc,
    string? CorrelationId,
    string? LastAction,
    string? LastReason,
    string? IconUrl = null,
    string? ShortName = null,
    string? Accent = null);

internal sealed record LifecycleFlowSnapshot(
    DateTimeOffset GeneratedAtUtc,
    IReadOnlyList<LifecycleFlowJourneyView> Journeys,
    IReadOnlyList<LifecycleFlowEventView> Events);

internal static class LifecycleFlowSnapshotBuilder
{
    public static LifecycleFlowSnapshot Build(
        IReadOnlyList<LifecycleRegistrationRecord> records,
        int limit,
        DateTimeOffset? now = null)
    {
        DateTimeOffset current = now ?? DateTimeOffset.UtcNow;
        int boundedLimit = Math.Clamp(limit, 10, 500);

        List<LifecycleFlowEventView> events = records
            .SelectMany(record => record.Audit.Select(audit =>
            {
                (string stage, string outcome) = Describe(audit.Action);
                return new LifecycleFlowEventView(
                    audit.AtUtc,
                    record.RegistrationId,
                    record.ApplicationId,
                    record.InstanceId,
                    record.InstallationId,
                    stage,
                    outcome,
                    audit.Action,
                    audit.Actor,
                    audit.CorrelationId,
                    audit.Reason,
                    audit.PreviousState?.ToString(),
                    audit.ResultingState.ToString());
            }))
            .OrderByDescending(x => x.AtUtc)
            .Take(boundedLimit)
            .ToList();

        List<LifecycleFlowJourneyView> journeys = records
            .Where(x => x.State != LifecycleRegistrationState.Purged)
            .Select(record => BuildJourney(record, current))
            .OrderByDescending(x => HealthWeight(x.Health))
            .ThenByDescending(x => x.LastEventAtUtc)
            .ToList();

        return new LifecycleFlowSnapshot(current, journeys, events);
    }

    private static LifecycleFlowJourneyView BuildJourney(
        LifecycleRegistrationRecord record,
        DateTimeOffset now)
    {
        string stage = record.State switch
        {
            LifecycleRegistrationState.Pending => "Approval",
            LifecycleRegistrationState.Approved => "Claim",
            LifecycleRegistrationState.Registered => "Registered",
            LifecycleRegistrationState.RecoveryPending => "Recovery Approval",
            LifecycleRegistrationState.RecoveryApproved => "Recovery Claim",
            LifecycleRegistrationState.IdentityConflict => "Identity Conflict",
            LifecycleRegistrationState.Rejected => "Rejected",
            LifecycleRegistrationState.Revoked => "Revoked",
            _ => "Request"
        };

        LifecycleAuditEvent? latest = record.Audit
            .OrderByDescending(x => x.AtUtc)
            .FirstOrDefault();

        LifecycleAuditEvent? stageEvent = FindStageEvent(record, stage) ?? latest;
        DateTimeOffset entered = stageEvent?.AtUtc ?? record.UpdatedAtUtc;
        long ageSeconds = Math.Max(0, (long)(now - entered).TotalSeconds);

        string health = DetermineHealth(record, stage, ageSeconds, latest, now);

        string? iconUrl = record.Metadata?["presentation"]?["iconUrl"]?.GetValue<string>();
        string? shortName = record.Metadata?["presentation"]?["shortName"]?.GetValue<string>();
        string? accent = record.Metadata?["presentation"]?["accent"]?.GetValue<string>();

        return new LifecycleFlowJourneyView(
            record.RegistrationId,
            record.ApplicationId,
            record.InstanceId,
            record.InstallationId,
            record.State.ToString(),
            stage,
            health,
            ageSeconds,
            entered,
            latest?.AtUtc,
            latest?.CorrelationId,
            latest?.Action,
            latest?.Reason,
            iconUrl,
            shortName,
            accent);
    }

    private static LifecycleAuditEvent? FindStageEvent(
        LifecycleRegistrationRecord record,
        string stage)
    {
        string[] actions = stage switch
        {
            "Approval" => ["RegistrationRequested"],
            "Claim" => ["Approved"],
            "Registered" => ["ConfigurationContractPublished", "Authenticated", "CredentialClaimed"],
            "Recovery Approval" => ["RecoveryRequested"],
            "Recovery Claim" => ["Approved"],
            "Identity Conflict" => ["IdentityConflict", "IdentityConflictAuthentication"],
            "Rejected" => ["Rejected"],
            "Revoked" => ["Revoked"],
            _ => ["RegistrationRequested"]
        };

        return record.Audit
            .Where(x => actions.Contains(x.Action, StringComparer.OrdinalIgnoreCase))
            .OrderByDescending(x => x.AtUtc)
            .FirstOrDefault();
    }

    private static string DetermineHealth(
        LifecycleRegistrationRecord record,
        string stage,
        long ageSeconds,
        LifecycleAuditEvent? latest,
        DateTimeOffset now)
    {
        if (record.State is LifecycleRegistrationState.IdentityConflict or
            LifecycleRegistrationState.Rejected or
            LifecycleRegistrationState.Revoked)
            return "Failed";

        LifecycleAuditEvent? latestContractEvent = record.Audit
            .Where(x =>
                string.Equals(x.Action, "ConfigurationContractPublished", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(x.Action, "ConfigurationContractRejected", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.AtUtc)
            .FirstOrDefault();

        if (string.Equals(
                latestContractEvent?.Action,
                "ConfigurationContractRejected",
                StringComparison.OrdinalIgnoreCase))
        {
            return "Failed";
        }

        if (record.State == LifecycleRegistrationState.Registered)
        {
            if (!record.LastAuthenticatedAtUtc.HasValue)
                return "Slow";

            if (latestContractEvent is null &&
                record.RegisteredAtUtc.HasValue &&
                now - record.RegisteredAtUtc.Value > TimeSpan.FromMinutes(2))
            {
                return "Slow";
            }

            long authAge = Math.Max(
                0,
                (long)(now - record.LastAuthenticatedAtUtc.Value).TotalSeconds);

            if (authAge > 1800) return "Stalled";
            if (authAge > 900) return "Slow";
            return "Healthy";
        }

        long slowAfter = stage switch
        {
            "Approval" or "Recovery Approval" => 300,
            "Claim" or "Recovery Claim" => 60,
            _ => 120
        };

        long stalledAfter = slowAfter * 3;

        if (ageSeconds >= stalledAfter) return "Stalled";
        if (ageSeconds >= slowAfter) return "Slow";
        return "Healthy";
    }

    private static (string Stage, string Outcome) Describe(string action)
    {
        return action switch
        {
            "RegistrationRequested" => ("Request", "Started"),
            "Approved" => ("Approval", "Succeeded"),
            "Rejected" => ("Approval", "Failed"),
            "CredentialClaimed" => ("Claim", "Succeeded"),
            "Authenticated" => ("Authenticate", "Succeeded"),
            "ConfigurationContractPublished" => ("Publish", "Succeeded"),
            "ConfigurationContractRejected" => ("Publish", "Failed"),
            "RecoveryRequested" => ("Recovery", "Started"),
            "Revoked" => ("Revoke", "Succeeded"),
            "Purged" => ("Purge", "Succeeded"),
            "IdentityConflict" or "IdentityConflictAuthentication" => ("Identity", "Failed"),
            "IdentityConflictResolvedRetainExisting" => ("Identity", "Resolved"),
            "IdentityConflictResolvedReplaceWithIncoming" => ("Identity", "Resolved"),
            _ => ("Lifecycle", "Observed")
        };
    }

    private static int HealthWeight(string health) => health switch
    {
        "Failed" => 4,
        "Stalled" => 3,
        "Slow" => 2,
        _ => 1
    };
}
