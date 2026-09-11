# Aegis.Configuration

Aegis.Configuration is the commissioning and configuration control plane for Aegis applications.

Applications declare what they require. Aegis.Configuration receives those contracts, evaluates configuration state, guides administrators through commissioning, and delegates secret storage and operational proof to the appropriate Common services.

## Core principles

- Applications are the authority for their configuration requirements.
- Applications publish their configuration contract to Aegis.Configuration on startup when a Configuration service URL is configured.
- Contract publication is best-effort: Aegis.Configuration is never a runtime dependency of the application.
- Missing configuration is a commissioning state, not an obscure startup failure.
- Configuration and secrets are distinct.
- Secret values are never written into commissioning reports.
- Common.Secrets owns secret-provider access.
- Common.Diagnostics provides bounded operational verification.
- Setup instructions are versioned independently from application binaries.
- A baseline instruction pack is available offline.
- Configuration status is explicit: NotRequired, Missing, Invalid, ConfiguredUnverified, Verified.

## Contract self-registration

Applications own their JSON configuration contract and package it with the deployable server host. At startup the host may publish that contract to:

`POST /api/contracts/register`

The request uses `X-Configuration-Registration-Key`. Aegis.Configuration validates the contract and authenticates it against the per-application secret:

`configuration/registration/<ApplicationId>`

A publisher can only authenticate as the ApplicationId whose secret it possesses. Registration is idempotent: unchanged contracts return the same SHA-256 contract hash without rewriting the stored contract; changed contracts atomically replace the current contract file.

The service still reads its configured contract directory, so file deployment remains a bootstrap/fallback option. Self-registration removes the need for routine manual copying.

A publishing failure must never prevent the application from starting or continuing to run. Applications use their own runtime configuration and Common.Secrets independently of Aegis.Configuration.

## Initial solution

- `Aegis.Configuration.Domain` - configuration contracts and state model.
- `Aegis.Configuration.Application` - discovery, evaluation, validation and commissioning orchestration.
- `Aegis.Configuration.Infrastructure` - contract/instruction persistence and provider adapters.
- `Aegis.Configuration.Web` - administrator commissioning UI and API.
- `Aegis.Configuration.Tests` - contract and commissioning tests.

The current vertical slice uses JSON application contracts. Applications can publish those same contracts directly to the service while retaining JSON as the source-of-truth artifact. A future shared package can expose the same domain contract directly from application code if that proves useful.
