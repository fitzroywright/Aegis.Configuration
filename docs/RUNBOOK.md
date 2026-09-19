# Aegis.Configuration Operational Runbook

## Purpose
Aegis.Configuration is the authoritative control-plane service for application registration lifecycle, configuration contracts, readiness metadata, and registration reconciliation.

## Dependencies
- PostgreSQL for durable registration/configuration state.
- Common.Security for operator authorization.
- Common.Diagnostics for health and diagnostic integration.
- Common.Secrets for provider-neutral secret access where required by the host.
- Aegis.Operations as an optional operational consumer/control surface.

## Startup
1. Load host configuration and secret-provider settings.
2. Open the Configuration database.
3. Apply/verify the expected schema.
4. Start registration/configuration endpoints.
5. Start reconciliation/background services.
6. Expose health and operational diagnostics.

## Shutdown
Stop the service normally and allow in-flight requests/background reconciliation to finish. Do not delete registration records or application identities during normal shutdown.

## Configuration
Configuration contracts contain setting names, required/optional state, metadata and safe readiness information. Sensitive values must not be returned by operational endpoints.

## Registration
Configuration is authoritative for lifecycle registration state. Preserve distinctions among Pending, Registered, RecoveryPending, Revoked, Rejected and IdentityConflict. Normal application restarts must not generate duplicate pending registrations.

## Secrets
Do not expose registration credentials, claim tokens, provider credentials, passwords, API keys, bearer tokens or complete sensitive connection strings through diagnostics, logs, APIs or this runbook.

## Health Verification
Verify:
- service health endpoint;
- database reachability;
- registration reconciliation;
- configuration-contract access;
- required background services.

HTTP reachability alone is not proof of full service health.

## Expected Diagnostics
Healthy requires current evidence for the service and its required dependencies. Missing or stale dependency evidence should be reported as Unknown/Unavailable rather than Healthy.

## Normal State
- database reachable;
- reconciliation healthy;
- no unexplained duplicate registrations;
- current application contracts readable;
- background reconciliation running.

## Common Warnings
- application contract incomplete;
- required application setting missing;
- registration waiting for approval/recovery;
- stale application metadata;
- optional dependency unavailable.

## Common Failures
- database unavailable;
- registration store unavailable;
- repeated reconciliation errors;
- invalid or revoked application credentials;
- identity conflict requiring operator review.

## Recovery
Restore the failed dependency first, then verify fresh post-recovery evidence. Do not infer recovery from a successful repair command alone.

## Backup
Back up the PostgreSQL registration/configuration database using the normal database backup policy. Include registration state because it is authoritative lifecycle data.

## Restore
Use the supplied registration backup/restore verification procedure against an isolated restore target before relying on a backup.

## Deployment
Deploy binaries/configuration without purging registration state. Validate database connectivity, health, reconciliation and contract access after deployment.

## Rollback
Roll back application binaries while preserving the database and registration records unless a schema rollback procedure explicitly requires otherwise.

## Purge
Registration purge is destructive administrative action. Purge only the intended registration and preserve the application InstallationId behavior expected by Common.Registration.

## Re-registration
After a legitimate purge, a known installation may create one fresh Pending registration. Repeated restarts must not create additional duplicates.

## Escalation
Escalate database restore failure, unexplained identity conflicts, repeated reconciliation failure, or evidence of registration credential misuse to the platform engineer.

## Log Locations
Use the service manager/journal and configured application logging destinations. Never log registration credentials, claim tokens or secret values.
