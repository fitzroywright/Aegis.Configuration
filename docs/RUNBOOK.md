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


## Contract lifecycle and failure details

Open **Contracts → Manage lifecycle** for an application. `Configuration.Manage` is required. The preview identifies each active or historical contract revision, its version and state. Enter a reason before selecting an action:

- **Retire** archives that revision and removes its requirements and linked published verification statuses from active evaluation. Registration is retained. A registered application remains in Applications with configuration marked Retired. Retirement is an administrator decision, not a consequence of temporary runtime downtime.
- **Restore** moves a selected historical revision back into evaluation. A newer active publication is never overwritten; review or retire it first if the historical revision is really needed.
- **Permanently delete this revision** deletes exactly the previewed contract file. The confirmation names that record. If publication changed it after preview, reload and review again. Registration identities, settings, secrets, application databases, runbooks, diagnostics and acceptance evidence are outside this operation.

**Contracts → Contract history** lists retained retired revisions and lifecycle audit events, including application owner, version, last publication, actor, reason and UTC time. Restored and deleted revisions remain represented in the lifecycle audit. Legacy archives have an unknown actor where older releases recorded none. Back up the entire configured contract directory, including `retired/` and `lifecycle-audit/`. Keep it on durable storage across redeployment. These are filesystem changes; no SQL schema migration is required. Run a single Configuration writer against this directory.

An authenticated current publication becomes active even if an earlier revision was retired. Earlier retired revisions remain historical. Contract retirement is not a persistent deny rule against an application's current registration.

Applications combines registration inventory and active contracts. A missing, unreadable or rejected contract does not remove a registered application. Registration, lifecycle health and configuration readiness appear separately; lifecycle health is not runtime or diagnostic health. If the registration database cannot be reached, readable contracts remain available and the authority failure is reported. Configuration cannot reconstruct previously unseen registrations while their authoritative database is unavailable.

Click an entire attention panel, failure banner, or requirement row to open its issue details. Tab and Enter activate links; a gold outline shows focus. Application rows also accept Enter or Space without intercepting child links. Requirement links carry the requirement ID; registration failures carry the registration ID. Details show the published cause, latest publication/attempt, and application-specific configuration/runbook/provider guidance. Re-evaluate configuration or retest the Configuration health assessment after a correction. Configuration does not invent an application diagnostic retest operation where no such operation exists. Browser Back retains Applications filters and scroll context.

Disabled optional secret capabilities do not produce provider or stale credential-verification warnings. Enabled capability failures still surface. `Secrets.Provider` missing from the application's published contract is a provider-identification failure; provider access, secret resolution and credential verification remain separate issues. Never paste secret values into contract display fields, retirement reasons, or audit notes.

### Cafeteria legacy alias

The source contains one explicitly superseded contract identity: `Cafeteria.Services`, replaced by `Aegis.Cafeteria.Services`. Older alias publication calls remain non-persisting for rolling-upgrade compatibility. On startup, Configuration archives the exact `Cafeteria.Services.configuration-contract.json` file only if a readable canonical `Aegis.Cafeteria.Services` contract exists. The migration is idempotent and preserves the canonical registration and evidence. No native Cafeteria seed definitions were present in the inspected Configuration/Operations main snapshots.

Before deleting any other Cafeteria-labelled persisted entry on Kratos, inventory it and identify its replacement. POS/POS.UI registrations, Common.Secrets settings, database connections and the Cafeteria Acceptance page are not automatically obsolete. This change does not delete those records. Use the revision preview and record the replacement/reason after resolving ambiguous identities.

Deployment acceptance still requires a Kratos inventory, restart/redeployment checks and mouse/keyboard checks in the deployed UI. Confirm current Cafeteria Services registration, published contracts and existing acceptance evidence before and after deployment. Verify RequestPortal appears with All Status and an empty search even when publication fails; correct its published rejection reason and confirm readiness/attention clear on reload. Source-level regression tests do not establish that a live provider or credential is healthy.
