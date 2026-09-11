# Aegis.Configuration

Aegis.Configuration is the commissioning and configuration control plane for Aegis applications.

Applications declare what they require. Aegis.Configuration discovers those contracts, evaluates configuration state, guides administrators through commissioning, and delegates secret storage and operational proof to the appropriate Common services.

## Core principles

- Applications are the authority for their configuration requirements.
- Missing configuration is a commissioning state, not an obscure startup failure.
- Configuration and secrets are distinct.
- Secret values are never written into commissioning reports.
- Common.Secrets owns secret-provider access.
- Common.Diagnostics provides operational verification.
- Setup instructions are versioned independently from application binaries.
- A baseline instruction pack is available offline.
- Configuration status is explicit: NotRequired, Missing, Invalid, ConfiguredUnverified, Verified.

## Initial solution

- `Aegis.Configuration.Domain` - configuration contracts and state model.
- `Aegis.Configuration.Application` - discovery, evaluation, validation and commissioning orchestration.
- `Aegis.Configuration.Infrastructure` - contract/instruction persistence and provider adapters.
- `Aegis.Configuration.Web` - administrator commissioning UI and API.
- `Aegis.Configuration.Tests` - contract and commissioning tests.

The first vertical slice uses a JSON application contract so applications can be commissioned before a shared contract package is adopted. A future shared package can expose the same domain contract directly from application code.
