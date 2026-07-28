# Data flow and trust boundaries

**Status:** Accepted for scaffolding by ADR-014–020

## Trust zones

| Zone | Examples | Trust rule |
|---|---|---|
| Z0 user intent | Confirmed scan/update/reset action | Authorizes only the displayed operation and immutable reviewed targets |
| Z1 standard-user core | Domain, orchestration, typed contracts | Trusted code, but all inputs are validated |
| Z2 local state | SQLite, settings, logs, cache records | Untrusted on read; never authority for execution/deletion |
| Z3 Windows/provider APIs | Registry, AppX, Task Scheduler, WinGet process/API | External boundary; bounded, parsed and failure-isolated |
| Z4 network sources | WinGet/Store metadata | Hostile input; schema/length/value validation and freshness required |
| Z5 elevated helper | One short-lived process | More privilege, less capability; authenticated narrow protocol only |
| Z6 filesystem targets | App-owned data roots and artifacts | Only fixed canonical roots; reparse points are hostile |

## Inventory and update-check flow

1. Z0 requests local-only or online scan.
2. Z1 obtains the single-scan lease and starts a correlation ID/cancellation scope.
3. Inventory adapters cross Z3 and return bounded raw records. Errors remain provider-scoped.
4. Z1 validates, normalizes and conservatively deduplicates while preserving install instances.
5. For online mode only, enabled connectors cross Z4 through Z3 adapters. They return validated snapshots with freshness.
6. Version/matching engines operate as pure Z1 rules. Conflict or uncertainty closes actionability.
7. Z1 writes a staging scan to Z2 and atomically promotes it. UI receives read models, never raw authority.

Validation points: field lengths and enums at adapter ingress; stable identity formation in domain; transaction and cancellation before promotion; stale/live label at presentation.

## User-approved update flow

1. UI displays installed/candidate identity, source, channel, architecture, confidence and expected action.
2. Explicit confirmation creates an immutable job in Z1; Z2 stores evidence but cannot alter the approved fields.
3. Runner revalidates the current mapping, version, source/provider allowlist and job state.
4. Non-elevated jobs execute through a fixed provider adapter using executable plus argument array.
5. If elevation is required, the desktop creates a current-user/SYSTEM-restricted random named pipe and launches the fixed signed helper through `runas`; bootstrap parameters contain no provider operation.
6. Z5 connects once, and both endpoints validate fixed signed process identity, PID, user/session, protocol and nonce. It accepts only the immutable job fields, rejects replay/unknown data and reconstructs the provider operation.
7. Bounded output and exit data return to Z1. Z5 exits.
8. Z1 runs targeted inventory verification. Only expected version evidence or reliable restart-pending produces a successful terminal interpretation.

No URL, executable path, working directory, environment block, output path or free-form arguments cross into execution authority.

## Artifact and deletion flow

1. Provider-owned downloads are never claimed as PC Steward artifacts.
2. App-owned artifacts are created beneath a fixed per-user root; records store a generated identifier and root-relative path.
3. Cleanup accepts a fixed category/operation. It resolves the configured root independently of SQLite.
4. Each target is canonicalized, checked for containment and inspected for reparse/mount behavior immediately before non-following deletion.
5. Active-job artifacts are excluded. Locked/residual items are reported individually.
6. Clear scan data uses a database transaction; Clear downloaded files uses only the app-owned download root; Factory reset writes a fixed marker and runs the compiled manifest before state opens on the next launch. A residual preserves reset-pending state.

## Scheduling and notification flow

The UI writes a typed current-user task definition through the scheduler adapter. Task Scheduler invokes the fixed `PCSteward.ScheduledHost` path without an update verb. That project has no Execution/ElevatedHelper reference, obtains the same scan lease, runs at the accepted lower-priority policy, optionally emits an attention/failure notification, and exits after completion/cancellation/final timeout.

## Export and diagnostics flow

Read models are projected from Z2, paths/device identity are redacted by default, output size is bounded, and the user chooses the destination. Components emit typed allowlisted events to the central sanitizer before bounded JSON-lines persistence. Diagnostic bundles are user-initiated and contain no credentials, tokens, licence keys, full environment dump or document content.

## Boundary invariants

- Network/source data can influence display and candidate evidence, never executable/path selection.
- Database tampering can at most cause a rejected or incorrect display state; runner/helper revalidation blocks execution.
- Elevation narrows the command surface rather than elevating the desktop application.
- Deletion authority is code-owned category plus fixed root, never record-owned path.
- Cancellation and crash boundaries preserve last-known-good inventory and append-only job evidence.
- Every cached result carries source and timestamp; stale is never represented as live.
