# ADR-019 — App-owned artifact roots

**Status:** Accepted
**Date:** 2026-07-28
**Decision owners:** Security reviewer and implementation reviewer
**Related requirements:** `FR-DAT-06–15`, `AC-DAT-06–16`, `TM-05`, `TM-06`, `TM-15`

## Context

Clear, retention and reset operations require exact ownership boundaries that cannot be redirected by metadata, database rows or user text.

## Options considered

### Option A — Compile-time categories beneath one stable per-user root

Authority is derived from code and each lifecycle category has an independent fixed child root.

### Option B — Database-registered or configurable absolute roots

This is flexible but lets tampered state become deletion authority.

## Decision

Accept Option A. The technical root is `%LocalAppData%\PCSteward`, derived from the Windows known-folder API plus literal components. Fixed children separate database/state, source cache, app-owned downloads, logs, settings/consent and reset marker. Temporary extraction created by PC Steward stays under the app-owned download root. User-chosen exports and Windows/WinGet/Store/provider caches are not app-owned roots.

Cleanup accepts a compiled operation/category enum, resolves the corresponding root independently of SQLite, and treats stored relative paths only as evidence. Before enumeration and immediately before non-following deletion, it verifies canonical component containment and rejects UNC/device paths, alternate streams, root equality/prefix confusion and every reparse/mount component. Active-job artifacts are excluded; residuals are relative and accurate. Factory reset uses the fixed manifest in ADR-013.

Option B is rejected because local database or configuration tampering could redirect deletion outside PC Steward.

## Consequences

### Positive

Ownership, retention and destructive scope are reviewable and cannot be expanded by provider data.

### Negative / trade-offs

Roots cannot be user-relocated in the MVP, and robust Windows handle-relative deletion remains complex.

### Follow-up controls and tests

Tasks 24/25/28 require relative/UNC/device/prefix/ADS, junction, symbolic-link, volume-mount, final/ancestor swap, locked-file, active-lease, idempotence, provider-cache exclusion and accurate partial-result tests. Hostile link/mount/race cases run only in approved disposable VMs.

## Reconsider when

A supported deployment model changes the known-folder root, or the reviewed full-walk primitive cannot close the ancestor race. Configurable or remote roots require a new security ADR.
