# ADR-014 — Solution and component boundaries

**Status:** Accepted
**Date:** 2026-07-28
**Decision owners:** Implementation reviewer and security reviewer
**Related requirements:** `NFR-MNT-01–03`, `NFR-MNT-05`, `AC-UPD-02`

## Context

Task 06 needs project boundaries that prevent UI, persistence, provider and privileged concerns from becoming one execution surface.

## Options considered

### Option A — Layered projects with capability-specific adapters and hosts

Dependency direction can be enforced and privileged/scheduled entry points can exclude unrelated capabilities.

### Option B — One desktop project with namespaces

This is initially smaller but cannot enforce the trust, execution and elevation boundaries through build references.

## Decision

Accept Option A. The solution contains:

- `PCSteward.Domain`: pure entities, value objects, deterministic rules and state machines; no I/O dependencies.
- `PCSteward.Application`: use cases and ports; depends only on Domain.
- `PCSteward.Persistence.Sqlite`, `PCSteward.Inventory.Windows`, `PCSteward.Provider.WinGet`, `PCSteward.Execution`, `PCSteward.Storage`, `PCSteward.Scheduling.Windows` and `PCSteward.Diagnostics`: capability-specific adapters depending inward on Application/Domain contracts.
- `PCSteward.Desktop`: WinUI presentation and the standard-user composition root. Presentation code uses application use cases, not SQLite/provider APIs.
- `PCSteward.ScheduledHost`: standard-user scan-only composition root; it has no reference to Execution or ElevatedHelper.
- `PCSteward.ElevatedHelper`: separately signed one-request host; it has no UI, persistence, inventory, scheduling or storage-cleanup reference.
- Separate unit, integration, architecture, security-fixture and performance test projects. Spike code is never referenced by production projects.

Option B is rejected because namespaces do not prevent an accidental UI-to-database call, scheduled update verb or privileged dependency.

## Consequences

### Positive

Build references express trust direction, capability-minimal hosts are testable, and adapters can change without changing domain behavior.

### Negative / trade-offs

The solution has more projects and explicit composition wiring.

### Follow-up controls and tests

Task 06 adds architecture tests for forbidden references and keeps warnings as errors. Tasks 21/22/26 prove that the runner, helper and scheduled host expose only their accepted capability.

## Reconsider when

A boundary creates a measured deployment blocker that cannot be solved by composition, and an ADR proves the replacement preserves the same capability isolation.
