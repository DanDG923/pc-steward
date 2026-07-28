# Component architecture

**Status:** Accepted for scaffolding by ADR-014–020
**Requirements:** G-01–G-10, FR-INV-01–09, FR-ID-01–07, FR-SRC-01–05/10, FR-UPD-01–11, FR-DAT-01–15, FR-RSC-01–05, NFR-MNT-01–05

## Architectural rules

1. Dependencies point inward: adapters and UI depend on application/domain contracts; domain code depends on neither Windows APIs, SQLite, processes nor UI.
2. Remote metadata, provider output and SQLite rows are untrusted data. None grants execution or deletion authority.
3. Execution is reconstructed from an allowlisted provider operation and immutable identifiers. No layer accepts an arbitrary command line or executable path.
4. Scans are standard-user, single-instance, cancellable and transactionally publish a new inventory only after collection succeeds sufficiently.
5. Elevation is an on-demand capability for one approved immutable job, never an ambient application state.
6. Deletion targets are internally derived from fixed application-owned roots and manifests, not database or metadata paths.
7. Deterministic domain rules alone decide version order, compatibility, confidence and actionability.

## Logical components

| Component | Responsibility | Must not do |
|---|---|---|
| Desktop UI | Onboarding, navigation, review, confirmations, progress, accessibility and display models | Query providers, construct commands, decide trust, access SQLite directly |
| Application orchestration | Use cases, job state machines, scan/update mutual exclusion, cancellation, transaction boundaries | Embed Windows/provider rules or bypass domain decisions |
| Domain rules | Canonical identities, install instances, version semantics, matching confidence, compatibility, policies and status transitions | Perform I/O or trust display-name similarity |
| SQLite persistence and migrations | Versioned schema, repositories, last-known-good snapshots, transactions and retention records | Store executable instructions or become an authorization source |
| Inventory providers | Read registry, MSIX/AppX and WinGet inventory into bounded raw records | Elevate, modify installed software, merge products |
| Update-source connectors | Fetch/parse enabled trusted-source metadata with timeouts, caching and provenance | Execute installers, follow arbitrary URLs, authorize matches |
| Version engine | Generic and connector-specific comparisons; explicit incomparable result | Guess ordering or silently cross channels |
| Matching engine | Deterministic evidence, compatibility checks, explanations and actionability threshold | Use AI or name similarity as execution authority |
| Non-elevated update runner | Execute a typed WinGet operation for an immutable approved job; capture bounded sanitized results | Invoke a shell, accept an executable path, mutate job identity |
| Short-lived elevated helper | Validate caller/session/request and execute one allowlisted elevated provider operation | Persist, accept arbitrary paths/flags/environment, read commands from SQLite |
| Artifact ownership and deletion | Track app-owned artifacts, enforce quota, active-use protection, canonical-root/reparse checks and accurate residuals | Delete provider-owned caches or accept arbitrary targets |
| Scheduling and notifications | Current-user Task Scheduler definitions, scan-only entry point, overlap prevention, attention notifications, process exit | Install updates or run a resident tray/service |
| Diagnostics | Correlation IDs, structured redacted events, retention and user-requested bundles | Capture secrets, full environment dumps or unbounded output |
| Performance harness | Repeatable fixtures and process-tree timing/CPU/memory/cancellation measurements | Change production safety thresholds to meet a budget |

## Accepted solution boundaries

- `PCSteward.Domain`: entities, value objects, pure rules and state machines.
- `PCSteward.Application`: use-case handlers and ports.
- `PCSteward.Persistence.Sqlite`: repositories, migrations and transactional units of work.
- `PCSteward.Inventory.Windows`: registry and MSIX/AppX adapters.
- `PCSteward.Provider.WinGet`: capability detection, inventory/source parsing and typed execution plan construction.
- `PCSteward.Execution`: non-elevated runner and result sanitization.
- `PCSteward.ElevatedHelper`: minimal separately signed entry point and IPC validation.
- `PCSteward.Storage`: artifact ownership, retention and safe cleanup.
- `PCSteward.Scheduling.Windows`: Task Scheduler and notification adapters.
- `PCSteward.Diagnostics`: logging/redaction and diagnostic export.
- `PCSteward.ScheduledHost`: scan-only standard-user entry point with no Execution or ElevatedHelper reference.
- `PCSteward.Desktop`: WinUI composition root and presentation.
- Separate unit, integration, security-fixture and performance projects. Spike projects remain outside production references.

These names and dependency direction are locked by ADR-014. Any weakening requires a superseding ADR.

## Core contracts

- Inventory providers return bounded `RawInventoryRecord` values plus warnings; they cannot persist.
- Connectors return `SourceSnapshot` with source identity, freshness and bounded records.
- Version comparison returns `Older`, `Equal`, `Newer` or `Incomparable`, with rationale.
- Matching returns evidence and confidence separately from `Actionability`; architecture/channel/scope conflicts force non-actionable.
- Update planning accepts only a user-approved candidate and emits an immutable job containing provider enum, source ID, native package ID and requested version.
- Provider execution accepts the immutable job and reconstructs a fixed executable plus argument list from trusted code.
- Cleanup accepts a fixed operation verb/category, not a path.

## Runtime composition

The standard-user desktop process owns UI and orchestration. Child provider processes are bounded by timeout, cancellation and captured-output limits. The elevated helper is a separately signed process launched from its fixed installed path only after confirmation and UAC; it connects to one identity-checked per-request named pipe, handles one immutable request and exits. Scheduled scans invoke `PCSteward.ScheduledHost` in the current-user context; that project cannot reference update execution and exits. No service or tray coordinator exists.

## State and consistency

- `PCSteward.Persistence.Sqlite` alone owns SQL, connections and schema. It uses ordered transactional migrations, foreign keys, tested WAL/durability policy, a per-user migration lease and pre-migration recovery.
- A scan writes a staging snapshot and atomically promotes it; cancellation/crash preserves the last known good snapshot.
- Stable decision keys live independently from scan rows so Clear scan data can preserve mappings/policies.
- Update jobs and events are append-oriented; provider exit is not Success until verification.
- Source snapshots include retrieval/success timestamps and stale state.
- Artifact records use root-relative paths only as evidence; deletion independently resolves a compiled category below `%LocalAppData%\PCSteward` and rejects reparse traversal.
- Logs are typed allowlisted events sanitized before bounded JSON-lines persistence; free-form provider objects and command lines are forbidden.

## Concurrency and lifecycle

- One full scan lease per profile.
- No full scan during update execution except a scoped post-update verification scan.
- Connector concurrency defaults to three; providers have independent timeouts.
- Batch updates are sequential; cancellation prevents the next job.
- Destructive controls require no active scan/update and an exclusive data-operation lease.
- Factory reset writes a fixed marker, closes state, and performs the compiled reset manifest before opening state on the next launch; residuals prevent a false first-run state.
- Closing the UI cancels or hands off only explicitly approved active work; otherwise every PC Steward process exits.

## Architecture fitness checks

- Dependency tests reject UI-to-SQLite/provider references and domain-to-adapter references.
- Source/SQLite fuzz fixtures cannot create commands, paths or actionable low-confidence candidates.
- Runner tests prove shell-free argument construction and allowlist rejection.
- Helper tests prove caller, nonce, job, verb, provider and identifier validation.
- Cleanup tests prove canonical-root and reparse containment.
- Process tests prove bounded concurrency, cancellation and no-resident-process requirements.
