# ADR-016 — Structured logging and redaction

**Status:** Accepted
**Date:** 2026-07-28
**Decision owners:** Implementation reviewer and privacy reviewer
**Related requirements:** `FR-HIS-03–06`, `NFR-OBS-01–05`, `AC-DAT-17`

## Context

Provider output, paths and machine data are useful for diagnosis but can contain hostile text or identifying information.

## Options considered

### Option A — Central allowlisted structured events sanitized before persistence

Every event has a known schema and redaction occurs before any local sink or export.

### Option B — Free-form component logs sanitized during export

This retains more detail but lets sensitive or unbounded content reach disk and makes reliable redaction impossible.

## Decision

Accept Option A. `PCSteward.Diagnostics` owns event IDs, severity, correlation fields, value bounds, sanitization, local JSON-lines persistence, retention and diagnostic export. Components emit typed allowlisted properties; they do not serialize raw provider objects, command lines, environment blocks, registry dumps or exceptions with unchecked data.

Paths and device identity are redacted before persistence by default. Provider output is length-bounded, control-character safe and classified as untrusted text. Logs rotate under the fixed log root and retain no more than 30 days or 100 MB. A user-requested export applies the sanitizer again and previews any explicit path inclusion.

Option B is rejected because export-time redaction cannot undo data already written to local logs.

## Consequences

### Positive

Privacy and injection handling are consistent, with bounded storage and deterministic diagnostic fixtures.

### Negative / trade-offs

Some failures require a second, explicitly consented export for additional path context.

### Follow-up controls and tests

Tasks 06/14/23/30 add analyzers or tests for forbidden logging shapes, hostile strings, credentials/tokens/licence keys, known folders, user names, device names, multiline/control data, retention and bundle size. Cancellation, denial, timeout and product/provider failures remain distinct events.

## Reconsider when

A mandatory failure cannot be diagnosed with the allowlisted schema and a narrower privacy-safe field is demonstrated.
