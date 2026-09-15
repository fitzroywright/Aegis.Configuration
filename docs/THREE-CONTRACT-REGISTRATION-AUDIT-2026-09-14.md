# Aegis Three-Contract Registration Audit

Date: 2026-09-14

## Platform rule

Every deployable Aegis application/service publishes three observational contracts to Aegis.Configuration:

1. **Configuration** — every setting the runtime actually consumes, including required/optional/conditional status, defaults, allowed sources, resolution precedence, effective source where directly knowable, and a secret indicator.
2. **Dependencies** — every service/runtime dependency explicitly supported by code evidence, including required/optional, startup/feature criticality, purpose, degraded behavior, interface, engine where explicitly known, endpoint where safe, and reachability where directly testable.
3. **Detected environment** — runtime-observed facts only: OS/version, CPU architecture/count, available memory, process working set, runtime version, network presence, free disk on relevant paths, writable-path probes, known-endpoint reachability, and hardware only when the application can actually enumerate it.

Aegis.Configuration remains a **description and monitoring system**. It does not own application configuration values and is not a configuration repository or secret store.

## Secret handling

- Secret values are never intentionally included in published contracts.
- Secret requirements publish metadata and configured/missing state only.
- `safeDisplayValue` and secret defaults are null for sensitive requirements.
- The Configuration receiver sanitizes sensitive requirements again before persistence (`SafeDisplayValue=null`, `DefaultValue=null`, `Redacted=true`).
- Secret0 is represented only as bootstrap presence/source metadata. Its value is never published.
- Registration credentials are never displayed.

## Dependency engine rule

- Engines are declared only from direct code/provider evidence.
- Connection strings are not parsed to guess database engines.
- Where a provider is explicit in code, the engine is declared (for example Npgsql -> PostgreSQL; Microsoft.Data.SqlClient -> Microsoft SQL Server; Microsoft.Data.Sqlite -> SQLite).
- If an ODBC path is introduced and its engine is intentionally abstract, the dependency contract must state that the interface is ODBC and the engine is unknown by design.

## Applications covered

### Aegis.Configuration

Publishes its own configuration, dependency and detected-environment contracts. Dependencies include the contract filesystem store, instruction-pack filesystem, Active Directory through Common.Security, and OpenBao only when configured. The self-description explicitly states that Configuration does not own application configuration values.

Receiver additions:
- aggregate `POST /api/registrations/register`
- `GET /api/dependencies`
- `GET /api/environments`
- per-application operational view with Configuration, Dependencies, dependency graph and Detected Environment
- sensitive-field sanitation before persistence
- separate `*.configuration-contract.json`, `*.dependency-contract.json`, and `*.environment-contract.json` persistence

### Aegis.Diagnostics

Outbound registration only was changed. Diagnostics architecture, execution, orchestration and target APIs were not redesigned.

Publishes its own settings and each explicitly configured diagnostics target. Dependencies include Configuration, configured diagnostics targets, OpenBao when used, and enabled notification channels. Machine credentials remain redacted.

### Aegis.Operations

Publishes Site, Environment, Configuration/Diagnostics URLs, request timeout and secret/bootstrap metadata. Dependencies are Configuration, Diagnostics, Active Directory/Common.Security, and OpenBao when used for registration secrets.

### Aegis.Cafeteria POS

Publishes local persistence, terminal/site identity, FFP integration behavior, logging, health, UI, Cafeteria Services, realtime, messaging, scan mappings and relevant secret requirements.

Dependencies include local SQLite, Cafeteria Services when enabled, Configuration, Diagnostics, optional Graylog and Messaging. The legacy/direct FFP database path does not infer an engine from its connection string; the contract states the provider/engine is determined by the selected code path rather than guessed from the value.

### Aegis.Cafeteria Services

Publishes service database, service URL, FFP SQL integration, AD/security, management roles, month opening, scheduled email, backup, logging, funding bootstrap, optional messaging/health and secret metadata.

Dependencies explicitly declare:
- SQLite service database (`Microsoft.Data.Sqlite` / EF Core)
- Microsoft SQL Server for FFP integration (`Microsoft.Data.SqlClient`, Windows Integrated Security)
- Active Directory (`LDAP/LDAPS via Common.Security`)
- Configuration and Diagnostics
- optional Graylog, SMTP, Common.Messaging and OpenBao

### Aegis.Studio

Publishes Configuration/Diagnostics URLs, PostgreSQL secret requirement, Microsoft Graph settings/client credential, notification webhooks, storage paths/limits, security settings and gallery behavior.

Dependencies explicitly declare PostgreSQL from Npgsql use, Microsoft Graph/Microsoft 365, Active Directory/Common.Security, filesystem storage, Configuration/Diagnostics and optional Slack/Teams.

### RequestPortal.Web

Publishes PostgreSQL secret requirement, dashboard thresholds, FFP Manager integration, storage/data-protection paths, messaging durability, upload/malware settings, SMTP/SMS/WhatsApp, staff OIDC, AD group-policy settings, OpenBao/Secret0, and optional public login-provider credentials.

Dependencies explicitly declare PostgreSQL from Npgsql use, filesystem/document storage, Configuration, production OpenBao, and conditionally FFP Manager Bridge, ClamAV, SMTP and staff OIDC provider.

### FfpManager.Bridge

Publishes bridge authentication, RequestPortal callback URL, artifact limit, gateway mode, SQL connection secret, supporting-document root, audit/status/document-type settings and reference refresh.

When SQL gateway mode is enabled, dependencies explicitly declare Microsoft SQL Server from `Microsoft.Data.SqlClient` and the supporting-document filesystem/share. RequestPortal is a direct HTTP dependency.

### Aegis.SensorNetwork Control Plane

Publishes control-plane database/admin-secret settings, Remote Door settings, OpenBao, site/identity and bootstrap metadata.

Dependencies explicitly declare PostgreSQL from Npgsql/EF Core, Configuration, Diagnostics and OpenBao.

### Aegis.SensorNetwork Local Management Client

Publishes client identity, Control Plane/profile-key configuration, local SQLite/photo/import settings, remote synchronization, telemetry paths, device supervision, legacy FFP compatibility and OpenBao/bootstrap settings.

Dependencies include local SQLite, Control Plane, Configuration/OpenBao and only **enabled explicitly configured devices**. Device interface/engine labels come from declared device types. The detected-environment contract does not claim hardware enumeration that the runtime cannot perform.

### Aegis.SensorNetwork FFP Sync

Publishes organization/client/subscriber identities, Control Plane URL, FFP SQL secret name, Control Plane/subscriber secret names and polling/reconciliation intervals.

Dependencies explicitly declare Microsoft SQL Server from `Microsoft.Data.SqlClient`, SensorNetwork Control Plane, Configuration and OpenBao.

### Aegis.SensorNetwork LocalAdmin

Publishes local bind URL, local-admin password secret, client installation/API credential, Control Plane reporting, local SQLite/photo/hash-key settings and OpenBao/bootstrap.

Dependencies are local SQLite, conditionally the Control Plane, Configuration and OpenBao.

### Aegis.SensorNetwork Display

Current code is an in-memory HTTP display actuator. It therefore publishes its bind URL and registration/bootstrap metadata but does **not** invent a database, external service or hardware dependency. Configuration is an optional observational dependency.

### Ebolito

Upgrades the existing configuration-only registration service to publish all three contracts. Publishes the optional PostgreSQL connection requirement, public URL/link lifetime, storage and messaging-channel settings plus redacted registration/bootstrap metadata.

When `ConnectionStrings:Ebolito` is absent, code explicitly selects the in-memory marketplace store and the dependency contract reports that engine instead of pretending PostgreSQL is required. When configured, PostgreSQL is declared from the Postgres store implementation, not inferred from the connection string.

## Detected-environment constraints

No application publishes declared environment requirements. Detection is limited to information the running process can directly observe or directly probe:
- OS description/version
- process architecture and logical processor count
- runtime version
- available managed-runtime memory figure when exposed by the runtime
- current process working set
- network presence
- relevant filesystem paths and safe write probes
- free/total disk for relevant filesystem roots
- known endpoint reachability where an HTTP/TCP probe is meaningful
- actual hardware only where enumeration exists in application code

No capacity sizing, minimum RAM/CPU/disk requirement, throughput estimate, guessed network requirement or inferred hardware is published.

## Compatibility

The existing configuration-only registration endpoint remains available for older publishers during migration. New publishers use the aggregate registration endpoint. Configuration contract storage is constrained to `*.configuration-contract.json` so dependency/environment files cannot be misread as configuration contracts.

## Verification status

This document describes the implementation branch set. Pull-request CI/build verification is required before merge. Any compile/runtime integration issue found by CI must be corrected on the feature branches before deployment.
