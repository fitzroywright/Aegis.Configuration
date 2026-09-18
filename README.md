# Aegis.Configuration

Aegis.Configuration is the configuration registry and administrator runbook service for Aegis applications.

Its job is deliberately narrow: **tell an administrator what an application requires, which settings are required or optional, and whether those settings have been supplied.** It does not decide whether the resulting system is operational.

## Core principles

- The application is the authority for its own configuration requirements.
- Each independently running application/component publishes its contract to Aegis.Configuration at startup.
- Publication is best-effort. Aegis.Configuration must never become a runtime dependency of the application.
- Every requirement declares whether it is **Required** or **Optional**.
- The application may publish a safe display value when appropriate and `IsConfigured` to report presence without exposing the value.
- Sensitive values are never accepted as display values and are never printed in runbooks.
- Aegis.Configuration may identify that OpenBao credentials or another secret are required, but it does not retrieve, display, test, or store the application's secret value.
- Required settings determine configuration completeness. Missing optional settings do not make configuration incomplete.
- Runtime reachability, authentication success, database availability, network connectivity, service health, logs, and operational telemetry are outside Aegis.Configuration.
- Runbooks are generated from the same contract the application publishes, reducing drift between documentation and the actual software.
- Site and instance identity may be published with the application contract so an administrator knows which installation is being viewed.

## Contract self-registration

Applications register with **Aegis.Operations**, which is the suite registration authority. Operations validates the application's real registration credential and forwards the sanitized configuration contract to Aegis.Configuration.

Normal applications use the Operations approval flow and the suite environment variable:

`AEGIS_REGISTRATION_KEY`

The control-plane services `Aegis.Operations`, `Aegis.Configuration`, and `Aegis.Diagnostics` are auto-provisioned only through the local Operations control-plane endpoint. They receive normal Operations registration records and real generated credentials; there is no separate Configuration bootstrap credential store.

Configuration machine endpoints under `/api/contracts/*` accept only calls authenticated as a currently registered `Aegis.Operations` instance. Configuration verifies that credential against Operations before changing contract state.

Registration is idempotent. Unchanged contracts return the same SHA-256 contract hash; changed contracts atomically replace the registered contract.

The repository does not ship fake or pre-populated application contracts. An empty Configuration installation correctly displays **NO APPLICATIONS REGISTERED** until real applications publish themselves.

## Administrator workflow

1. Install/copy an Aegis application.
2. Start it.
3. The application authenticates to Operations; Operations forwards the configuration contract when Aegis.Configuration is available.
4. Open Aegis.Configuration and select the application/instance.
5. Review required and optional settings and identify missing required values.
6. Supply the missing configuration using the application's deployment instructions.
7. Restart/reload the application when required.
8. Confirm Configuration reports all required settings supplied.
9. Open the generated Configuration Runbook for installation/configuration guidance and sign-off.
10. Use the separate operational diagnostics system to determine whether the configured application and its dependencies actually work.

## UI standard

The administrator UI uses **Compact LCARS**: LCARS visual language with information-dense tables, thinner chrome, responsive tablet/phone layouts, and generous touch targets. LCARS curved elements retain their Back-navigation behavior.

The UI is observational. It does not expose service restart or infrastructure-control actions.

## Solution

- `Aegis.Configuration.Domain` - published configuration contracts and configuration state.
- `Aegis.Configuration.Application` - completeness evaluation, runbook generation, history and configuration-change logic.
- `Aegis.Configuration.Infrastructure` - contract/instruction persistence and Configuration's own provider adapters.
- `Aegis.Configuration.Web` - Compact LCARS administrator UI and registration/report API.
- `Aegis.Configuration.Tests` - contract, completeness and runbook tests.
