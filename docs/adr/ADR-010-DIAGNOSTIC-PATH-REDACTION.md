# ADR-010 — Diagnostic path redaction

**Status:** Accepted
**Date:** 2026-07-28
**Decision owners:** Product owner and implementation reviewer
**Related requirements:** `FR-DAT-03`, `FR-HIS-05–06`, `AC-DAT-17`
**Related open decision:** `OD-10`

## Context

Installation paths can aid diagnosis but reveal user names, folders and machine-specific information.

## Options considered

### Option A — Include paths by default

Higher diagnostic detail with unnecessary privacy exposure.

### Option B — Redact by default, explicit include option

Local UI may retain detail; exported bundle minimizes disclosure.

## Decision

Accept Option B. Diagnostic exports redact paths and device identity by default. Explicit path inclusion requires a preview and warning for that export only; it does not change the logging policy.

## Consequences

Support may request a second export; privacy-by-default improves.

### Follow-up controls and tests

Central redaction, known-folder replacement, traversal-safe export destination and secret/path fixtures.

## Evidence the upcoming work must collect

- Task 14 CSV/JSON path and warning behavior.
- Task 23 diagnostic sanitizer fixtures.
- Task 30 release privacy review and secret scan.

## Reconsider when

Redaction makes mandatory failures undiagnosable and a narrower safe representation is proven.
